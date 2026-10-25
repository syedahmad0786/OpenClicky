using OpenClicky.App.Speech;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Speech;
using OpenClicky.Input;

namespace OpenClicky.App;

/// <summary>Talk feedback in the shell: captions, the muted-speaker card and the microphone-trouble card.</summary>
internal sealed partial class TrayApplicationContext
{
    /// <summary>1 from a turn's start until its first reply sentence has had the speaker checked; any thread.</summary>
    private int _speakerCheckArmed;
    /// <summary>UI thread: the endpoint the muted card is about, this turn's final reply text, and whether the card waits for it.</summary>
    private string? _mutedEndpointId, _replyText;
    private bool _copyReplyPending;
    /// <summary>The microphone of the newest local recognizer (a held talk press creates its own).</summary>
    private NAudioMicrophoneRecorder? _sttMic;
    private System.Windows.Forms.Timer? _micWatch;
    /// <summary>UI thread: Environment.TickCount64 when the talk shortcut started recording (its Pressed).</summary>
    private long _talkPressedAt;

    private NAudioMicrophoneRecorder NewSttMic() => _sttMic = new NAudioMicrophoneRecorder(() => _settings.MicrophoneDeviceId);

    private void WireTalkFeedback()
    {
        // Previews raise no words, so they never show captions.
        _tts.WordSpoken += word => { if (_settings.Captions) OnUi(() => _shell?.PostCaptionWord(word)); };
        // SpeechEnded fires per sentence: the page's 1.5 s debounce keeps the bar while the next sentence follows.
        _tts.SpeechEnded += () => OnUi(() => { if (CaptionsPolicy.ClearOnSpeechEnded(_talk.State)) _shell?.ClearCaptions(); });
        _tts.ReplySpeechStarting += CheckSpeakerBeforeReply;
        _talk.StateChanged += state =>
        {
            // Raised in order with the turn's speech, so arming here (not via the UI queue) precedes the first sentence.
            if (state is CompanionVoiceState.Processing or CompanionVoiceState.Idle)
                Interlocked.Exchange(ref _speakerCheckArmed, state == CompanionVoiceState.Processing ? 1 : 0);
            OnUi(() =>
            {
                if (state == CompanionVoiceState.Processing) { _replyText = null; _copyReplyPending = false; _replyClipboard = new(); }
                if (CaptionsPolicy.ClearOnState(state)) _shell?.ClearCaptions();
            });
        };
        _talk.AssistantResponseChanged += (text, complete) =>
        {
            if (complete) OnUi(() =>
            {
                _replyText = text;
                if (_copyReplyPending && CopyReply()) _shell?.UpdateCard(TalkCards.MutedSpeaker(copied: true));
            });
        };
        _hotkey.Pressed += (_, _) => _talkPressedAt = Environment.TickCount64;
        _talk.HeldTalkStarted += WatchMicDuringHold;
        // A cancelled release (Esc, another key): subscribed before _talk.Start(), so this runs before the orchestrator's
        // handler, and before the watch's next tick.
        _hotkey.Released += (_, e) => { if (e is ShortcutEndedEventArgs { Cancelled: true }) CancelMicWatch(); };
    }

    /// <summary>Worker thread, once per turn before its first sentence plays: a muted or silent output gets a card and the reply on the clipboard. It still speaks.</summary>
    private void CheckSpeakerBeforeReply()
    {
        if (Interlocked.Exchange(ref _speakerCheckArmed, 0) == 0) return;
        if (RenderDevices.ReadVolume(_settings.OutputDeviceId) is not { } output || !MutedSpeakerPolicy.ShouldWarn(output.Muted, output.Volume)) return;
        Log($"speaker_silent muted={output.Muted} volume={output.Volume:0.00}");
        OnUi(() =>
        {
            if (_shell is not { IsActive: true }) return; // the card says the answer was copied: no card, no copy
            _mutedEndpointId = output.Id;
            // The "copied" sentence appears only once the copy worked; streaming: the full reply arrives after the first sentence.
            if (_replyText is not null) _shell.AddCard(TalkCards.MutedSpeaker(copied: CopyReply()));
            else { _shell.AddCard(TalkCards.MutedSpeaker()); _copyReplyPending = true; }
        });
    }

    /// <summary>Puts this turn's reply on the clipboard; false when there is none or the clipboard refused it.</summary>
    private bool CopyReply()
    {
        _copyReplyPending = false;
        if (string.IsNullOrWhiteSpace(_replyText)) return false;
        try { _replyClipboard.CaptureOnce(); Clipboard.SetText(_replyText, TextDataFormat.UnicodeText); return true; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        { Log("speaker_silent_copy_error=" + ex.GetType().Name); return false; }
    }

    /// <summary>
    /// A revealed talk hold: sample the recording microphone every 100 ms until listening ends (the level also drives the
    /// companion's listening bars). A hold of 3 s or more (from
    /// the press) that never reached 0.01 gets the microphone card. Only NAudio recorders have a level; with any other
    /// recognizer it is skipped. A cancelled hold (Esc, Stop) disposes the watch first, so it never warns.
    /// </summary>
    private void WatchMicDuringHold()
    {
        CancelMicWatch();
        if (_talk.State != CompanionVoiceState.Listening) return;
        var pressedAt = _talkPressedAt;
        long heldMs = 0;
        var maxPeak = 0f;
        var measurable = true;
        var timer = _micWatch = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            if (_talk.State == CompanionVoiceState.Listening)
            {
                var mic = RecordingMic();
                if (mic is null) measurable = false;
                else
                {
                    maxPeak = Math.Max(maxPeak, mic.PeakLevel);
                    _overlay.ShowMicLevel(mic.PeakLevel);
                }
                heldMs = Environment.TickCount64 - pressedAt;
                return;
            }
            timer.Dispose();
            if (ReferenceEquals(_micWatch, timer)) _micWatch = null;
            if (!measurable || !MicTroublePolicy.ShouldWarn(heldMs, maxPeak)) return;
            Log($"mic_silent held_ms={heldMs}");
            _shell?.AddCard(TalkCards.MicTrouble());
        };
        timer.Start();
    }

    bool IShellHost.CardAction(string cardId, string actionId)
    {
        switch (cardId, actionId)
        {
            case (TalkCards.MutedSpeakerId, TalkCards.VolumeAction):
                if (_mutedEndpointId is not { } endpoint || !RenderDevices.SetVolume(endpoint, 0.5f))
                    _shell?.AddToast("I couldn't change the volume. Use the volume control on the taskbar.");
                return false;
            case (TalkCards.MicTroubleId, TalkCards.MicSettingsAction):
                _shell?.OpenNotch(NotchTab.Settings, "microphone");
                return false;
            case (TalkActionText.CopyCardId, TalkActionText.UndoAction):
                UndoTalkCopy();
                return false;
            // The step card stays up: the walkthrough's next change replaces it (or, once stopped, removes it).
            case (CompanionCards.WalkthroughId, CompanionCards.BackAction):
                _ = MoveWalkthroughAsync(WalkthroughMove.Back);
                return true;
            case (CompanionCards.WalkthroughId, CompanionCards.RecheckAction):
                _ = MoveWalkthroughAsync(WalkthroughMove.Refresh);
                return true;
            case (CompanionCards.WalkthroughId, CompanionCards.StopAction):
                StopWalkthrough();
                return false;
            // Save keeps the drawing; Clear takes it off the screen; dismissing the card leaves it up, unsaved.
            case (CompanionCards.DrawingSaveId, CompanionCards.SaveAction):
                SaveLastDrawing();
                return false;
            case (CompanionCards.DrawingSaveId, CompanionCards.ClearAction):
                _talk.ClearDrawing();
                return false;
            // Drive consent is answered only here, by the user's click on the card; the Esc pill's Stop ends the task.
            case (CompanionCards.DriveConsentId, CompanionCards.DriveAction):
                AnswerDriveConsent(true);
                return false;
            case (CompanionCards.DriveConsentId, CompanionCards.NotNowAction):
                AnswerDriveConsent(false);
                return false;
            case (CompanionCards.DrivingId, CompanionCards.StopAction):
                CancelTask();
                OpenControl.Resume();
                return false;
            // The card stays up while the last phrase is delivered; ending dictation removes it.
            case (DictationCards.ActivityId, DictationCards.StopAction):
                if (!_dictationActive || _dictationHeld) return false;
                _ = FinishHandsFreeDictationAsync();
                return true;
            case (DictationCards.LearnedId, DictationCards.UndoAction):
                UndoLearnedWord();
                return false;
            case (DictationCards.TidyNoticeId, DictationCards.TurnOffAction):
                NotchSettings.Patch(DictationCards.TidyNoticeOffKey, System.Text.Json.JsonSerializer.SerializeToElement(false));
                return false;
            case (DictationCards.TidyNoticeId, DictationCards.OkAction):
                return false;
            default:
                Log("shell: card action not handled: " + actionId);
                return false;
        }
    }

    /// <summary>Closing the consent card is a "not now"; closing the hands-free card is a Stop; closing any other card changes nothing.</summary>
    void IShellHost.CardDismissed(string cardId)
    {
        if (cardId == CompanionCards.DriveConsentId) AnswerDriveConsent(false);
        else if (cardId == DictationCards.ActivityId && _dictationActive && !_dictationHeld) _ = FinishHandsFreeDictationAsync();
    }

    /// <summary>The NAudio recorder that is capturing now (the shared one, else the local recognizer's), if any has a level.</summary>
    private NAudioMicrophoneRecorder? RecordingMic() =>
        _mic is NAudioMicrophoneRecorder { IsRecording: true } shared ? shared
        : _sttMic is { IsRecording: true } live ? live : null;

    private void CancelMicWatch()
    {
        _micWatch?.Dispose();
        _micWatch = null;
    }

    /// <summary>Every tray cancel of the talk orchestrator: the watch goes first, so a stopped hold never warns.</summary>
    private void CancelTalk()
    {
        CancelMicWatch();
        _talk.Cancel();
    }

    private void DisposeTalkFeedback() => CancelMicWatch();
}

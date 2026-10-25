using OpenClicky.Agent;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>
/// Hold and hands-free dictation: the gate, recording, delivery through <see cref="DictationFlow"/> (tidy-up, verified insert,
/// clipboard fallback) and the activity card. The classic dictation panel is used only while the shell is off.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    private const string DictationBusy = "Stop the current voice turn or task before dictating.";
    private const string DictationNeedsWhisper = "Automatic dictation requires the local Whisper model. Download it in Settings → Voice first.";

    private bool _dictationHeld, _dictationShown;
    private string? _dictationHoldError;
    private long _dictationPress;
    /// <summary>UI thread: identifies the running dictation (its field may be null: the words then go to the clipboard).</summary>
    private object? _dictationSession;
    private bool _dictationHandsFree;
    /// <summary>UI thread: the activity card is up (the shell was active when dictation showed), so the classic panel is not.</summary>
    private bool _dictationCardShown;
    private System.Windows.Forms.Timer? _dictationLiveThrottle;
    private string? _dictationLive, _dictationLiveShown;

    /// <summary>A dictate hold still inside its tap window: recording silently, nothing shown yet.</summary>
    private bool DictationHidden => _dictationHeld && !_dictationShown;

    /// <summary>
    /// Why dictation cannot start now (null when it can). <paramref name="target"/> is the focused field, or null when the
    /// focused control cannot take text: that dictation still records and delivers to the clipboard. Shows nothing.
    /// </summary>
    private string? DictationBlocker(out DictationTarget? target)
    {
        target = null;
        if (_talk.State != CompanionVoiceState.Idle || _talk.ConversationActive || _taskCts is not null) return DictationBusy;
        if (DictationWhisperPlan() is null) return DictationNeedsWhisper;
        DictationTarget.TryCapture(out var captured, out var failure, out _, AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra));
        var gate = DictationGate.Check(failure);
        Log($"dictation_gate failure={failure} proceed={gate.Proceed} field={gate.WithField}");
        if (!gate.Proceed) return gate.RefusalMessage;
        target = gate.WithField ? captured : null;
        return null;
    }

    private void ReportDictationBlocker(string error)
    {
        if (error == DictationBusy) { _tray.ShowBalloonTip(3000, "OpenClicky dictation", error, ToolTipIcon.Info); return; }
        _tray.ShowBalloonTip(5000, error == DictationNeedsWhisper ? "Local dictation unavailable" : "Dictation target unavailable", error, ToolTipIcon.Warning);
        ShowClassic();
        ClassicStatus(error);
    }

    private void BeginDictationAtCurrentTarget()
    {
        if (DictationBlocker(out var target) is { } error) { ReportDictationBlocker(error); return; }
        StopWalkthrough();
        StartDictation(target);
    }

    /// <summary>Hands-free (and Home's Dictate into app): a phrase is delivered as each ends, until stopped.</summary>
    private void StartDictation(DictationTarget? target)
    {
        BeginDictationSession(target, handsFree: true);
        ShowDictationUi();
        _talk.StartConversation();
    }

    private void BeginDictationSession(DictationTarget? target, bool handsFree)
    {
        _dictationTarget = target;
        _dictationActive = true;
        _dictationHandsFree = handsFree;
        _dictationSession = new object();
    }

    /// <summary>The activity card when the shell is active, else the classic dictation panel.</summary>
    private void ShowDictationUi()
    {
        if (_shell is { IsActive: true } shell)
        {
            _dictationCardShown = true;
            _dictationLive = _dictationLiveShown = null;
            shell.AddCard(DictationCards.Activity(_dictationHandsFree, null));
            return;
        }
        var target = _dictationTarget;
        _home.BeginDictation(target?.AppTitle ?? "No text field", target?.ControlName ?? "your words go to the clipboard", automatic: true);
    }

    /// <summary>UI thread: the live transcript on the activity card, at most 4 updates a second (the newest text always lands).</summary>
    private void ShowDictationLive(string text)
    {
        if (!_dictationCardShown) return;
        _dictationLive = text;
        if (_dictationLiveThrottle is not null) return; // its next tick shows the newest text
        PushDictationLive();
        var timer = _dictationLiveThrottle = new System.Windows.Forms.Timer { Interval = 250 };
        timer.Tick += (_, _) =>
        {
            if (_dictationLive != _dictationLiveShown) { PushDictationLive(); return; }
            timer.Dispose();
            if (ReferenceEquals(_dictationLiveThrottle, timer)) _dictationLiveThrottle = null;
        };
        timer.Start();
    }

    private void PushDictationLive()
    {
        _dictationLiveShown = _dictationLive;
        _shell?.UpdateCard(DictationCards.Activity(_dictationHandsFree, _dictationLive));
    }

    /// <summary>Takes the activity card down; false when it was not up (the classic panel was used instead).</summary>
    private bool EndDictationActivity()
    {
        _dictationLiveThrottle?.Dispose();
        _dictationLiveThrottle = null;
        if (!_dictationCardShown) return false;
        _dictationCardShown = false;
        _shell?.RemoveCard(DictationCards.ActivityId);
        return true;
    }

    /// <summary>
    /// The orchestrator's interceptor during dictation: each finished phrase is tidied up, fitted and typed into the field once
    /// every key is up. Anything not verified in the field (or with no field, or keys still held) goes to the clipboard.
    /// </summary>
    private async Task<bool> HandleDictationVoiceAsync(string text, CancellationToken token)
    {
        var session = _dictationSession;
        var target = _dictationTarget;
        Log($"dictation_result chars={text.Length} empty={string.IsNullOrWhiteSpace(text)}");
        if (string.IsNullOrWhiteSpace(text)) return true;
        // Never type while a modifier is down (Ctrl+Enter would send): still held after 10 s, nothing is typed.
        var keysUp = await WaitForModifiersReleasedAsync(token).ConfigureAwait(false);
        var field = keysUp && target is not null
            ? new UiaDictationField(target, _executor, () => AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra))
            : null;
        var copied = true;
        var delivery = await DictationFlow.DeliverAsync(text, field, CreateDictationCleaner(),
            new(_settings.DictationCleanup, _settings.DictionaryWords ?? []),
            words => copied = CopyDictationToClipboard(words), token).ConfigureAwait(false);
        Log($"dictation_delivery kind={delivery.Kind} verified={delivery.Insert?.Verified} keys_up={keysUp} field={field is not null} copied={copied}");
        OnUi(() => ShowDictationDelivery(session, target, delivery, copied));
        return true;
    }

    /// <summary>The user's Talk provider and model, or null when tidy-up is off or no provider is configured.</summary>
    private IDictationCleaner? CreateDictationCleaner() =>
        _settings.DictationCleanup && ProviderFactory.CreateTalkClient(_settings, _secrets, Log) is { } client
            ? new TalkClientDictationCleaner(client, _settings.ResolveTalkModel())
            : null;

    /// <summary>Any thread: sets the clipboard on the UI (STA) thread and waits for it; false when the clipboard refused.</summary>
    private bool CopyDictationToClipboard(string words)
    {
        var copied = false;
        try
        {
            _uiContext.Send(_ =>
            {
                try { Clipboard.SetText(words, TextDataFormat.UnicodeText); copied = true; }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
                { Log("dictation_clipboard_error=" + ex.GetType().Name); }
            }, null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.InvalidAsynchronousStateException or ObjectDisposedException)
        { Log("dictation_clipboard_error=" + ex.GetType().Name); } // the UI thread is gone (shutting down)
        return copied;
    }

    /// <summary>UI thread. Cards and toasts never show the dictated words.</summary>
    private void ShowDictationDelivery(object? session, DictationTarget? target, DictationDelivery delivery, bool copied)
    {
        var current = _dictationActive && ReferenceEquals(session, _dictationSession) && !_dictationCardShown;
        switch (delivery.Kind)
        {
            case DictationDeliveryKind.Inserted:
                if (target is not null) OnDictationInserted(target, delivery);
                if (current)
                {
                    _home.AcceptDictationTranscript(delivery.Text);
                    _home.SetDictationResult(delivery.Insert!.Message + " Keep speaking, or click the companion to stop.");
                }
                break;
            case DictationDeliveryKind.Clipboard when !copied:
                const string lost = "I couldn't reach the clipboard, so those words were not kept. Try dictating again.";
                if (_shell is { IsActive: true } shell) shell.AddToast(lost);
                else _tray.ShowBalloonTip(5000, "OpenClicky dictation", lost, ToolTipIcon.Warning);
                if (current) _home.SetDictationResult(lost);
                break;
            case DictationDeliveryKind.Clipboard:
                var card = DictationCards.Clipboard();
                if (_shell is { IsActive: true } active) active.AddCard(card);
                else _tray.ShowBalloonTip(card.AutoDismissMs ?? 8000, card.Title, card.Body ?? "", ToolTipIcon.Info);
                if (current)
                {
                    _home.AcceptDictationTranscript(delivery.Text);
                    _home.SetDictationResult(card.Title + ". " + card.Body);
                }
                break;
        }
    }

    /// <summary>A verified insert (any thread's delivery, posted to the UI thread). D6 watches the field for a learned correction.</summary>
    private void OnDictationInserted(DictationTarget target, DictationDelivery delivery)
    {
    }

    /// <summary>
    /// Dictate shortcut down: check and capture the target and record, all silently. The dictation UI (or why it cannot
    /// start) shows only once the hold passes the 400 ms tap window or is released normally.
    /// </summary>
    private async void BeginHeldDictation()
    {
        if (_dictationActive || _dictationHeld) return;
        try
        {
            _dictationHeld = true;
            _dictationShown = false;
            var press = ++_dictationPress;
            _dictationHoldError = DictationBlocker(out var target);
            if (_dictationHoldError is null)
            {
                BeginDictationSession(target, handsFree: false);
                _talk.BeginHeldListening(); // picks the local Whisper recognizer because _dictationActive is set
            }
            await Task.Delay(_talk.HeldRevealDelay);
            if (press == _dictationPress) ShowHeldDictation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("dictation_hold_start_error=" + ex.GetType().Name);
            if (_dictationActive) CancelDictation();
            _dictationHeld = false;
        }
    }

    private void ShowHeldDictation()
    {
        if (!_dictationHeld || _dictationShown) return;
        _dictationShown = true;
        if (_dictationHoldError is { } error)
        {
            _dictationHeld = false;
            ReportDictationBlocker(error);
            return;
        }
        if (!_dictationActive) return;
        if (_talk.Walkthrough.View.Active) StopWalkthrough();
        ShowDictationUi();
        _talk.RevealHeldListening();
    }

    /// <summary>Dictate shortcut up. Cancelled inside the tap window: no trace. Otherwise the phrase is transcribed and
    /// delivered once every key is physically up (see <see cref="WaitForModifiersReleasedAsync"/>), then dictation closes.</summary>
    private async void EndHeldDictation(bool cancelled)
    {
        if (!_dictationHeld) return;
        _dictationPress++; // the reveal timer of this press is moot
        var session = _dictationSession;
        bool Same() => _dictationActive && session is not null && ReferenceEquals(session, _dictationSession);
        try
        {
            if (cancelled)
            {
                if (Same()) CancelDictation(); // silent while still hidden
                _dictationHeld = false;
                return;
            }
            ShowHeldDictation();
            _dictationHeld = false;
            if (!Same()) return;
            await _talk.EndListeningAsync();
            if (Same()) CancelDictation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("dictation_hold_error=" + ex.GetType().Name);
            if (Same()) CancelDictation();
            _dictationHeld = false;
        }
    }

    /// <summary>Hands-free shortcut: the dictation flow with a phrase delivered as each ends, until the next double-tap.</summary>
    private void ToggleHandsFreeDictation()
    {
        if (_dictationHeld) return; // a hold owns this dictation; its release ends it
        if (_dictationActive) _ = FinishHandsFreeDictationAsync();
        else BeginDictationAtCurrentTarget();
    }

    private async Task FinishHandsFreeDictationAsync()
    {
        var session = _dictationSession;
        bool Same() => _dictationActive && session is not null && ReferenceEquals(session, _dictationSession);
        try
        {
            _talk.StopConversationAfterCurrentTurn(); // no new phrase after this one
            if (_talk.State == CompanionVoiceState.Listening) await _talk.EndListeningAsync();
            // ponytail: polls for the in-flight delivery to finish (max 30 s); a TalkOrchestrator "turn finished" task if this needs precision.
            for (var waited = 0; _talk.State != CompanionVoiceState.Idle && waited < 30_000; waited += 50) await Task.Delay(50);
            if (!Same()) return;
            if (_talk.State != CompanionVoiceState.Idle) Log("dictation_finish_timeout");
            CancelDictation();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("dictation_finish_error=" + ex.GetType().Name);
            if (Same()) CancelDictation();
        }
    }

    /// <summary>Never type while Ctrl, Alt, Shift or Win is physically down (Ctrl+Enter would send). False if still held after 10 s.</summary>
    private static async Task<bool> WaitForModifiersReleasedAsync(CancellationToken token)
    {
        int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];
        for (var waited = 0; waited < 10_000; waited += 30)
        {
            if (!modifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0)) return true;
            await Task.Delay(30, token).ConfigureAwait(false);
        }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

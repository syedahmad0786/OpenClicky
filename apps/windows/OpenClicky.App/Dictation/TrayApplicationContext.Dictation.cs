using OpenClicky.Agent;
using OpenClicky.Core;
using OpenClicky.Core.Dictation;
using OpenClicky.Core.Settings;
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
    private DictationRun? _dictationSession;
    private bool _dictationHandsFree;
    /// <summary>UI thread: the activity card is up (the shell was active when dictation showed), so the classic panel is not.</summary>
    private bool _dictationCardShown;
    private System.Windows.Forms.Timer? _dictationLiveThrottle;
    private string? _dictationLive, _dictationLiveShown;
    /// <summary>UI thread: everything this dictation has put on the clipboard (a verified insert adds nothing), so a later
    /// phrase never overwrites an earlier one; and whether the clipboard notice was shown for it.</summary>
    private string _dictationClipboard = "";
    private bool _dictationClipboardNoticed;
    /// <summary>UI thread: some of this dictation's clipboard text was typed but never confirmed in the field.</summary>
    private bool _dictationClipboardUnconfirmed;

    /// <summary>One dictation: its hands-free delivery order and cancellation, and the last phrase delivered.</summary>
    private sealed class DictationRun(bool handsFree, bool shellUi)
    {
        /// <summary>Hands-free phrases are delivered here, in order and one at a time, so the microphone never waits on them.</summary>
        public readonly SerialQueue Deliveries = new();
        /// <summary>Cancelled when the dictation is cancelled (Esc, another key): pending deliveries stop.</summary>
        public readonly CancellationTokenSource Cancel = new();
        public bool HandsFree { get; } = handsFree;
        /// <summary>Tidy-up and correction learning run only with the shell: the classic UI has no toggle, disclosure or Undo.</summary>
        public bool ShellUi { get; } = shellUi;
        /// <summary>The last phrase delivered (inserted or copied): the seam for a phrase whose field context cannot be read.</summary>
        public volatile string? LastDelivered;
    }

    /// <summary>A dictate hold still inside its tap window: recording silently, nothing shown yet.</summary>
    private bool DictationHidden => _dictationHeld && !_dictationShown;

    /// <summary>
    /// Why dictation cannot start now (null when it can). <paramref name="target"/> is the focused field, or null when the
    /// focused control cannot take text: that dictation still records and delivers to the clipboard. Shows nothing.
    /// </summary>
    private string? DictationBlocker(out DictationTarget? target)
    {
        target = null;
        if (_talk.State != CompanionVoiceState.Idle || _talk.ConversationActive || TaskOpen) return DictationBusy;
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
        if (_shell is { IsActive: true } shell) { shell.AddToast(error); return; } // the classic Home window stays closed
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
        _dictationSession = new DictationRun(handsFree, _shell is { IsActive: true });
        StopDictationWatch();
        _dictationClipboard = "";
        _dictationClipboardNoticed = _dictationClipboardUnconfirmed = false;
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
    /// The orchestrator's interceptor during dictation. A hold delivers its phrase before returning; hands-free queues each
    /// phrase (in order, one at a time) and returns at once, so the microphone listens again while the phrase is tidied and typed.
    /// A transcript that only echoes the dictionary prompt is dropped.
    /// </summary>
    private async Task<bool> HandleDictationVoiceAsync(string text, CancellationToken token)
    {
        var run = _dictationSession;
        var target = _dictationTarget;
        var echo = WhisperPrompt.IsEcho(text, WhisperPrompt.FromDictionary(_settings.DictionaryWords ?? []));
        Log($"dictation_result chars={text.Length} empty={string.IsNullOrWhiteSpace(text)} echo={echo}");
        if (string.IsNullOrWhiteSpace(text) || echo) return true;
        if (run is not { HandsFree: true })
        {
            await DeliverDictationPhraseAsync(run, target, text, token).ConfigureAwait(false);
            return true;
        }
        _ = run.Deliveries.Enqueue(async () =>
        {
            try { await DeliverDictationPhraseAsync(run, target, text, run.Cancel.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Log("dictation_delivery_cancelled"); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("dictation_delivery_error=" + ex.GetType().Name); }
        });
        return true;
    }

    /// <summary>
    /// One phrase: tidied up (shell only), fitted and typed into the field once every key is up. Anything not verified in the
    /// field (or with no field, or keys still held) goes to the clipboard.
    /// </summary>
    private async Task DeliverDictationPhraseAsync(DictationRun? run, DictationTarget? target, string text, CancellationToken token)
    {
        // Never type while a modifier is down (Ctrl+Enter would send): still held after 10 s, nothing is typed.
        var keysUp = await WaitForModifiersReleasedAsync(token).ConfigureAwait(false);
        var field = keysUp && target is not null
            ? new UiaDictationField(target, _executor, () => AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra))
            : null;
        var copied = true;
        var tidy = run is { ShellUi: true } && _settings.DictationCleanup;
        var delivery = await DictationFlow.DeliverAsync(text, field, tidy ? CreateDictationCleaner() : null,
            new(tidy, _settings.DictionaryWords ?? [], FallbackBefore: run?.LastDelivered),
            words => copied = CopyDictationToClipboard(run, words), token).ConfigureAwait(false);
        if (run is not null && delivery.Kind != DictationDeliveryKind.Nothing) run.LastDelivered = delivery.Text;
        Log($"dictation_delivery kind={delivery.Kind} verified={delivery.Insert?.Verified} sent={delivery.Insert?.InputSent} keys_up={keysUp} field={field is not null} copied={copied}");
        OnUi(() => ShowDictationDelivery(run, target, delivery, copied));
    }

    /// <summary>The user's Talk provider and model, or null when tidy-up is off or no provider is configured.</summary>
    private IDictationCleaner? CreateDictationCleaner() =>
        _settings.DictationCleanup && ProviderFactory.CreateTalkClient(_settings, _secrets, Log) is { } client
            ? new NoticedCleaner(new TalkClientDictationCleaner(client, _settings.ResolveTalkModel()), () => OnUi(ShowTidyNoticeOnce))
            : null;

    /// <summary>Calls <paramref name="invoked"/> as each tidy-up starts, just before the transcript is sent.</summary>
    private sealed class NoticedCleaner(IDictationCleaner inner, Action invoked) : IDictationCleaner
    {
        public Task<string?> CleanAsync(string raw, DictationFieldContext? context, IReadOnlyList<string> dictionary, CancellationToken ct)
        {
            invoked();
            return inner.CleanAsync(raw, context, dictionary, ct);
        }
    }

    /// <summary>UI thread: the first time tidy-up runs, says what goes to the provider and offers Turn off. Shown once.</summary>
    private void ShowTidyNoticeOnce()
    {
        if (_settings.DictationCleanupNoticeShown || _shell is not { IsActive: true } shell) return;
        NotchSettings.Change(s => s.DictationCleanupNoticeShown = true); // a failed save toasts; the notice then shows again next time
        shell.AddCard(DictationCards.TidyNotice());
    }

    /// <summary>
    /// Any thread: on the UI (STA) thread, puts this dictation's clipboard text with <paramref name="words"/> fitted after it on
    /// the clipboard, and waits for it; false when the clipboard refused (the session's text then stays as it was).
    /// </summary>
    private bool CopyDictationToClipboard(DictationRun? session, string words)
    {
        var copied = false;
        try
        {
            _uiContext.Send(_ =>
            {
                var same = session is not null && ReferenceEquals(session, _dictationSession);
                var whole = DictationTextFit.Append(same ? _dictationClipboard : "", words);
                try { Clipboard.SetText(whole, TextDataFormat.UnicodeText); copied = true; }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
                { Log("dictation_clipboard_error=" + ex.GetType().Name); }
                if (copied && same) _dictationClipboard = whole;
            }, null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.InvalidAsynchronousStateException or ObjectDisposedException)
        { Log("dictation_clipboard_error=" + ex.GetType().Name); } // the UI thread is gone (shutting down)
        return copied;
    }

    /// <summary>UI thread. Cards and toasts never show the dictated words.</summary>
    private void ShowDictationDelivery(DictationRun? session, DictationTarget? target, DictationDelivery delivery, bool copied)
    {
        var current = _dictationActive && ReferenceEquals(session, _dictationSession) && !_dictationCardShown;
        switch (delivery.Kind)
        {
            case DictationDeliveryKind.Inserted:
                if (target is not null) OnDictationInserted(session, target, delivery);
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
                // Once per dictation: later phrases join the same clipboard text, so the notice is refreshed, never stacked.
                // Typed-but-unconfirmed words may already be in the field: that warning stays for the rest of the dictation.
                var sameSession = session is not null && ReferenceEquals(session, _dictationSession);
                var unconfirmed = delivery.Insert?.InputSent == true;
                if (sameSession) unconfirmed = _dictationClipboardUnconfirmed |= unconfirmed;
                var card = unconfirmed ? DictationCards.ClipboardUnconfirmed() : DictationCards.Clipboard();
                var noticed = sameSession && _dictationClipboardNoticed;
                if (sameSession) _dictationClipboardNoticed = true;
                if (_shell is { IsActive: true } active)
                {
                    if (noticed) active.UpdateCard(card);
                    else active.AddCard(card);
                }
                else if (!noticed) _tray.ShowBalloonTip(card.AutoDismissMs ?? 8000, card.Title, card.Body ?? "", ToolTipIcon.Info);
                if (current)
                {
                    _home.AcceptDictationTranscript(delivery.Text);
                    _home.SetDictationResult(card.Title + ". " + card.Body);
                }
                break;
        }
    }

    private const int DictationWatchPollMs = 750, DictationWatchMaxMs = 15_000;
    /// <summary>UI thread: cancels the running correction watch (a new dictation, a newer insert, a learned word).</summary>
    private CancellationTokenSource? _dictationWatch;
    private string? _learnedWord;

    private void StopDictationWatch()
    {
        _dictationWatch?.Cancel();
        _dictationWatch = null;
    }

    /// <summary>
    /// UI thread, after a verified insert: for 15 s, watches the field (locally; its text is never logged or sent) for the user
    /// fixing one misheard word, and learns that word. A newer dictation, or the field changing hands, ends the watch.
    /// </summary>
    private void OnDictationInserted(DictationRun? session, DictationTarget target, DictationDelivery delivery)
    {
        StopDictationWatch();
        var words = _settings.DictionaryWords ?? [];
        if (_dictationSession is not null && !ReferenceEquals(session, _dictationSession)) return; // a newer dictation owns the field now
        if (session is not { ShellUi: true }) return; // no learning in the classic UI: it has no Undo
        if (words.Count >= SettingsCatalog.MaxDictionaryWords) return;
        if (delivery.Insert is not { Verified: true, ValueBefore: { } before, ValueAfter: { } after }) return;
        var detector = new DictationCorrectionDetector(before, after, delivery.Text, [.. words]);
        var cts = _dictationWatch = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            var reason = "timeout";
            try
            {
                for (var waited = 0; waited < DictationWatchMaxMs; waited += DictationWatchPollMs)
                {
                    await Task.Delay(DictationWatchPollMs, token).ConfigureAwait(false);
                    if (target.ReadValueWhileFocused() is not { } value) { reason = "field_left"; break; }
                    if (detector.Observe(value) is not { } word)
                    {
                        if (!detector.IsSpent) continue;
                        reason = "span_lost";
                        break;
                    }
                    reason = "learned";
                    OnUi(() => LearnDictationWord(word, token));
                    break;
                }
            }
            catch (OperationCanceledException) { reason = "cancelled"; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { reason = ex.GetType().Name; }
            Log("dictation_watch_end reason=" + reason);
        }, token);
    }

    /// <summary>UI thread: adds the word through the settings patch (cap and cleaning apply, the page updates) and offers Undo.</summary>
    private void LearnDictationWord(string word, CancellationToken token)
    {
        if (token.IsCancellationRequested || _shell is not { IsActive: true } shell) return; // the card's Undo needs the shell
        var words = _settings.DictionaryWords ?? [];
        if (words.Count >= SettingsCatalog.MaxDictionaryWords || words.Contains(word, StringComparer.OrdinalIgnoreCase)) return;
        if (!SetDictationDictionary([.. words, word]) || !(_settings.DictionaryWords ?? []).Contains(word, StringComparer.Ordinal)) return;
        _learnedWord = word;
        Log("dictation_word_learned");
        shell.AddCard(DictationCards.Learned(word));
    }

    /// <summary>UI thread: the Undo on the learned-word card forgets the word again.</summary>
    private void UndoLearnedWord()
    {
        var word = _learnedWord;
        _learnedWord = null;
        if (word is null) return;
        SetDictationDictionary((_settings.DictionaryWords ?? []).Where(w => !string.Equals(w, word, StringComparison.OrdinalIgnoreCase)));
    }

    private bool SetDictationDictionary(IEnumerable<string> words) =>
        NotchSettings.Patch("dictation.dictionary", System.Text.Json.JsonSerializer.SerializeToElement(words.ToArray())) == PatchResult.Applied;

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
            if (_talk.State != CompanionVoiceState.Idle) Log("dictation_finish_timeout");
            // The queued phrases are delivered, in order, before dictation ends; each is bounded (keys 10 s, tidy-up 6 s).
            if (session is not null)
            {
                try { await session.Deliveries.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (TimeoutException) { Log("dictation_finish_delivery_timeout"); }
            }
            if (!Same()) return;
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

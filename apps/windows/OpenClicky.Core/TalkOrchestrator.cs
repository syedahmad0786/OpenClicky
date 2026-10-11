namespace OpenClicky.Core;

/// <summary>One cancellable voice or text turn at a time, sharing the same screen-aware pipeline.</summary>
public sealed class TalkOrchestrator : IDisposable
{
    private readonly IPushToTalkMonitor _hotkey;
    private readonly IOverlayController _overlay;
    private readonly IScreenCaptureService _capture;
    private readonly IMicrophoneRecorder _mic;
    private readonly Func<ISttClient?> _sttFactory;
    private readonly Func<ITalkClient?> _talkFactory;
    private readonly ITtsClient _tts;
    private readonly Func<AppSettings> _settings;
    private readonly Func<string, CancellationToken, Task>? _agentRunner;
    private readonly Action<string> _log;
    private readonly List<(string user, string assistant)> _history = new();
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private CancellationTokenSource? _turnCts;
    private CompanionVoiceState _state;
    private bool _disposed;

    public TalkOrchestrator(IPushToTalkMonitor hotkey, IOverlayController overlay,
        IScreenCaptureService capture, IMicrophoneRecorder mic, Func<ISttClient?> sttFactory,
        Func<ITalkClient?> talkFactory, ITtsClient tts, Func<AppSettings> settings,
        Func<string, CancellationToken, Task>? agentRunner = null, Action<string>? log = null)
    {
        _hotkey = hotkey;
        _overlay = overlay;
        _capture = capture;
        _mic = mic;
        _sttFactory = sttFactory;
        _talkFactory = talkFactory;
        _tts = tts;
        _settings = settings;
        _agentRunner = agentRunner;
        _log = log ?? Console.WriteLine;
    }

    public CompanionVoiceState State => _state;
    public event Action<CompanionVoiceState>? StateChanged;
    public event Action<string, string>? MessageReceived;

    public void Start()
    {
        if (_settings().PersistConversationHistory)
        {
            _history.AddRange(ConversationHistoryStore.Load());
            foreach (var (user, assistant) in _history)
            {
                MessageReceived?.Invoke("You", user);
                MessageReceived?.Invoke("OpenClicky", assistant);
            }
        }
        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        try { _hotkey.Start(); }
        catch (Exception ex) { ReportAssistantMessage($"Voice shortcut unavailable: {ex.Message}. You can still type here."); }
    }

    public void ClearHistory()
    {
        Cancel();
        try { lock (_history) { _history.Clear(); ConversationHistoryStore.Clear(); } }
        catch (Exception ex) { ReportAssistantMessage($"Could not clear saved history: {ex.Message}"); }
    }

    public void ReportAssistantMessage(string text) => MessageReceived?.Invoke("OpenClicky", text);

    private void SetState(CompanionVoiceState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    public void Cancel()
    {
        var wasListening = _state == CompanionVoiceState.Listening;
        var turn = Interlocked.Exchange(ref _turnCts, null);
        try { turn?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (wasListening) turn?.Dispose();
        _tts.Stop();
        if (_mic.IsRecording)
        {
            try { _mic.StopToWav(); }
            catch (Exception ex) { _log($"mic_stop_error={ex.GetType().Name}"); }
        }
        SetState(CompanionVoiceState.Idle);
        _overlay.Hide();
    }

    public void BeginListening()
    {
        if (_disposed || _state == CompanionVoiceState.Listening) return;
        Cancel();
        _turnCts = new CancellationTokenSource();
        try
        {
            _mic.Start();
            SetState(CompanionVoiceState.Listening);
            _overlay.ShowListening();
        }
        catch (Exception ex)
        {
            Cancel();
            ReportAssistantMessage($"Could not start your microphone: {ex.Message}. Type your request instead.");
        }
    }

    public async Task EndListeningAsync()
    {
        if (_state != CompanionVoiceState.Listening || _turnCts is not { } turn) return;
        byte[] wav;
        try { wav = _mic.StopToWav(); }
        catch (Exception ex)
        {
            Cancel();
            ReportAssistantMessage($"Could not read your microphone: {ex.Message}");
            return;
        }
        await RunTurnAsync(null, wav, turn).ConfigureAwait(false);
    }

    public Task SubmitTextAsync(string text, bool remember = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        Cancel();
        var turn = _turnCts = new CancellationTokenSource();
        return RunTurnAsync(text.Trim(), Array.Empty<byte>(), turn, remember);
    }

    private void OnPressed(object? sender, EventArgs e) => BeginListening();
    private async void OnReleased(object? sender, EventArgs e) => await EndListeningAsync();

    private async Task RunTurnAsync(string? transcript, byte[] wav, CancellationTokenSource turn, bool remember = true)
    {
        var token = turn.Token;
        var entered = false;
        try
        {
            await _turnGate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            token.ThrowIfCancellationRequested();
            SetState(CompanionVoiceState.Processing);
            _overlay.ShowProcessing();
            var settings = _settings();
            if (transcript is null)
            {
                transcript = settings.DebugTranscriptOverride?.Trim();
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    if (wav.Length <= 44)
                    {
                        ReportAssistantMessage("I didn't catch any audio. Hold the shortcut while you speak, or type here.");
                        return;
                    }
                    var stt = _sttFactory();
                    if (stt is null)
                    {
                        ReportAssistantMessage("Choose Windows speech or add an OpenRouter key in Settings → Voice. You can also type here.");
                        return;
                    }
                    transcript = (await stt.TranscribeAsync(wav, token).ConfigureAwait(false)).Trim();
                }
            }
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                ReportAssistantMessage("I couldn't understand that. Please try again or type your request.");
                return;
            }
            MessageReceived?.Invoke("You", transcript);
            _log($"request_chars={transcript.Length}");
            if (AgentIntent.TryExtract(transcript, out var agentTask))
            {
                if (string.IsNullOrWhiteSpace(agentTask)) ReportAssistantMessage("Tell me what you'd like done in Agent mode.");
                else if (_agentRunner is null) ReportAssistantMessage("Agent mode is unavailable in this build.");
                else await _agentRunner(agentTask, token).ConfigureAwait(false);
                return;
            }

            var talk = _talkFactory();
            if (talk is null)
            {
                ReportAssistantMessage("Continue with ChatGPT in Settings to ask about your screen. The pointing demo works before sign-in.");
                return;
            }
            _overlay.Hide();
            var screens = await Task.Run(() => _capture.CaptureAllMonitorsJpegAsync(token), token).ConfigureAwait(false);
            _overlay.ShowProcessing();
            List<(string, string)> history;
            lock (_history) history = _history.SelectMany(h => new[] { ("user", h.user), ("assistant", h.assistant) }).ToList();
            var raw = await talk.CompleteVisionTalkAsync(CompanionPrompts.TalkSystemPrompt, history,
                transcript, screens, settings.ResolveVisionModel(), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var parsed = PointParser.Parse(raw);
            var spoken = parsed.SpokenText;
            ReportAssistantMessage(spoken);
            lock (_history)
            {
                token.ThrowIfCancellationRequested();
                if (remember) _history.Add((transcript, spoken));
                if (_history.Count > 10) _history.RemoveAt(0);
                if (remember && settings.PersistConversationHistory)
                {
                    try { ConversationHistoryStore.Save(_history); }
                    catch (Exception ex) { _log($"history_save_error={ex.GetType().Name}"); }
                }
            }
            var mapped = CoordinateMapper.ToVirtualScreen(parsed, screens);
            if (mapped is { } point)
            {
                SetState(CompanionVoiceState.Pointing);
                _overlay.PointAt(point.virtualX, point.virtualY, parsed.ElementLabel, spoken);
                _log($"point=({point.virtualX},{point.virtualY})");
            }
            else
            {
                SetState(CompanionVoiceState.Responding);
                _overlay.ShowResponding(spoken);
            }
            if (settings.SpeakReplies) await _tts.SpeakAsync(spoken, token).ConfigureAwait(false);
            else await Task.Delay(1800, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { _log("turn_cancelled"); }
        catch (Exception ex)
        {
            _log($"turn_error={ex.GetType().Name}");
            if (!token.IsCancellationRequested) ReportAssistantMessage($"Couldn't finish this request: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _turnCts, null, turn), turn))
            {
                SetState(CompanionVoiceState.Idle);
                _overlay.Hide();
            }
            if (entered) _turnGate.Release();
            turn.Dispose();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        Cancel();
        _hotkey.Dispose();
        _overlay.Dispose();
        _mic.Dispose();
    }
}

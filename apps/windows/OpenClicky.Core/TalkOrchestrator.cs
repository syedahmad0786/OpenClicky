namespace OpenClicky.Core;

/// <summary>
/// Phase B Talk path: PTT → mic → STT → screen capture → vision LLM → POINT → TTS.
/// </summary>
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
    private CancellationTokenSource? _turnCts;
    private CompanionVoiceState _state = CompanionVoiceState.Idle;

    public TalkOrchestrator(
        IPushToTalkMonitor hotkey,
        IOverlayController overlay,
        IScreenCaptureService capture,
        IMicrophoneRecorder mic,
        Func<ISttClient?> sttFactory,
        Func<ITalkClient?> talkFactory,
        ITtsClient tts,
        Func<AppSettings> settings,
        Func<string, CancellationToken, Task>? agentRunner = null,
        Action<string>? log = null)
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

    public void Start()
    {
        LoadPersistedHistory();
        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        try
        {
            _hotkey.Start();
            _log($"openclicky talk ready — hold {_hotkey.Chord.DisplayName}");
        }
        catch (Exception ex)
        {
            _log($"hotkey_start_error={ex.Message} — set a different chord in Settings or relaunch as a normal user session");
        }
    }

    public void ClearHistory()
    {
        _history.Clear();
        try
        {
            ConversationHistoryStore.Clear();
            _log("history_cleared");
        }
        catch (Exception ex)
        {
            _log($"history_clear_error={ex.Message}");
        }
    }

    private void LoadPersistedHistory()
    {
        var settings = _settings();
        if (!settings.PersistConversationHistory)
        {
            return;
        }

        try
        {
            var loaded = ConversationHistoryStore.Load();
            _history.Clear();
            _history.AddRange(loaded);
            if (_history.Count > 0)
            {
                _log($"history_loaded turns={_history.Count}");
            }
        }
        catch (Exception ex)
        {
            _log($"history_load_error={ex.Message}");
        }
    }

    private void PersistHistoryIfEnabled()
    {
        var settings = _settings();
        if (!settings.PersistConversationHistory)
        {
            return;
        }

        try
        {
            ConversationHistoryStore.Save(_history);
        }
        catch (Exception ex)
        {
            _log($"history_save_error={ex.Message}");
        }
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        _turnCts?.Cancel();
        _tts.Stop();
        _state = CompanionVoiceState.Listening;
        _overlay.ShowListening();
        try
        {
            _mic.Start();
        }
        catch (Exception ex)
        {
            _log($"mic_start_error={ex.Message}");
        }

        _log("ptt_pressed → listening");
    }

    private async void OnReleased(object? sender, EventArgs e)
    {
        _state = CompanionVoiceState.Processing;
        _overlay.ShowProcessing();
        _log("ptt_released → processing");

        byte[] wav = Array.Empty<byte>();
        try
        {
            wav = _mic.StopToWav();
        }
        catch (Exception ex)
        {
            _log($"mic_stop_error={ex.Message}");
        }

        _turnCts?.Cancel();
        _turnCts = new CancellationTokenSource();
        var token = _turnCts.Token;
        var settings = _settings();

        try
        {
            var transcript = settings.DebugTranscriptOverride?.Trim();
            if (string.IsNullOrEmpty(transcript))
            {
                var stt = _sttFactory();
                if (stt is null)
                {
                    _log("stt_unavailable — need OpenRouter key for mic STT, or DebugTranscriptOverride");
                    _overlay.ShowResponding(
                        "for microphone transcription, add an openrouter api key in settings, or set a debug transcript. chatgpt sign-in covers talk and agent replies.");
                    await Task.Delay(2500, token).ConfigureAwait(false);
                    return;
                }

                if (wav.Length < 44)
                {
                    _log("stt_skipped empty_audio");
                    _overlay.ShowResponding("i didn't catch any audio. hold the hotkey and speak, then release.");
                    await Task.Delay(2500, token).ConfigureAwait(false);
                    return;
                }

                transcript = (await stt.TranscribeAsync(wav, token).ConfigureAwait(false)).Trim();
            }

            _log($"transcript={transcript}");
            if (string.IsNullOrWhiteSpace(transcript))
            {
                _overlay.ShowResponding("i couldn't understand that. try again?");
                await Task.Delay(2000, token).ConfigureAwait(false);
                return;
            }

            if (AgentIntent.TryExtract(transcript, out var agentTask))
            {
                _log($"agent_intent task={agentTask}");
                if (string.IsNullOrWhiteSpace(agentTask))
                {
                    const string needTask =
                        "agent mode is ready, but i need a task. try saying openclicky agent, then what you want done.";
                    _state = CompanionVoiceState.Responding;
                    _overlay.ShowResponding(needTask);
                    await _tts.SpeakAsync(needTask, token).ConfigureAwait(false);
                    return;
                }

                if (_agentRunner is null)
                {
                    var spokenAgent =
                        $"agent mode heard you, but the agent runner is not wired yet. task was: {agentTask}";
                    _state = CompanionVoiceState.Responding;
                    _overlay.ShowResponding(spokenAgent);
                    await _tts.SpeakAsync(spokenAgent, token).ConfigureAwait(false);
                    return;
                }

                _state = CompanionVoiceState.Processing;
                await _agentRunner(agentTask, token).ConfigureAwait(false);
                return;
            }

            var talk = _talkFactory();
            if (talk is null)
            {
                _log("talk_unavailable — missing provider auth");
                _overlay.ShowResponding(
                    "add an openrouter api key or continue with chatgpt in settings to enable talk.");
                await Task.Delay(2500, token).ConfigureAwait(false);
                return;
            }

            // Hide buddy so capture does not include the overlay triangle/bubble.
            _overlay.Hide();
            var screens = await _capture.CaptureAllMonitorsJpegAsync(token).ConfigureAwait(false);
            _overlay.ShowProcessing();
            var primary = screens.FirstOrDefault(c => c.IsCursorMonitor) ?? screens.FirstOrDefault();
            if (primary is not null)
            {
                _log($"capture_bytes={primary.JpegBytes.Length} magic={(primary.LooksLikeJpeg ? "FFD8" : "????")}");
            }

            var history = _history
                .SelectMany(h => new[] { ("user", h.user), ("assistant", h.assistant) })
                .ToList();

            var raw = await talk.CompleteVisionTalkAsync(
                CompanionPrompts.TalkSystemPrompt,
                history,
                transcript,
                screens,
                settings.ResolveVisionModel(),
                token).ConfigureAwait(false);

            var parsed = PointParser.Parse(raw);
            var spoken = parsed.SpokenText;
            _log($"reply={spoken}");

            _history.Add((transcript, spoken));
            if (_history.Count > 10)
            {
                _history.RemoveAt(0);
            }

            PersistHistoryIfEnabled();

            var mapped = CoordinateMapper.ToVirtualScreen(parsed, screens);
            if (mapped is { } point)
            {
                _state = CompanionVoiceState.Pointing;
                _overlay.PointAt(point.virtualX, point.virtualY, parsed.ElementLabel);
                _log($"point=({point.virtualX},{point.virtualY}) label={parsed.ElementLabel}");
            }

            _state = CompanionVoiceState.Responding;
            _overlay.ShowResponding(spoken);
            await _tts.SpeakAsync(spoken, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log("turn cancelled");
        }
        catch (Exception ex)
        {
            _log($"talk_error={ex.GetType().Name}: {ex.Message}");
            try
            {
                _overlay.ShowResponding("something went wrong talking to the model. check the log in settings.");
                await _tts.SpeakAsync("something went wrong. check settings.", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore secondary failures
            }
        }
        finally
        {
            _state = CompanionVoiceState.Idle;
            _overlay.Hide();
            _log("idle");
        }
    }

    public void Dispose()
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;
        _turnCts?.Cancel();
        _turnCts?.Dispose();
        _tts.Stop();
        _hotkey.Dispose();
        _overlay.Dispose();
        _mic.Dispose();
    }
}

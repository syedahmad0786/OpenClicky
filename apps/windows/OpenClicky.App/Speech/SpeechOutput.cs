using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Speech;

namespace OpenClicky.App.Speech;

/// <summary>
/// Speaks replies with the voice, speed and output device in Settings → Voice, read again for every sentence so a change
/// applies to the next one. Sentences of one reply are 0.35 s apart; <see cref="Stop"/> cuts playback and drops whatever is
/// waiting. An OpenAI voice that fails falls back to a Windows voice for that sentence.
/// Thread safety: callable from any thread; <see cref="WordSpoken"/> and <see cref="SpeechEnded"/> are raised on worker
/// threads (subscribers marshal to the UI themselves).
/// </summary>
internal sealed class SpeechOutput : ITtsClient
{
    public const string PreviewText = "Hi! This is how I sound.";
    private readonly Func<AppSettings> _settings;
    private readonly ISecretStore _secrets;
    private readonly Action<string> _toast;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _playing = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource _stop = new();
    private long _lastEndTicks; // Environment.TickCount64 when the last sentence ended; 0 = never
    private int _fallbackToastShown;
    private readonly OpenAiVoiceBreaker _openAi = new();

    /// <param name="toast">Shows a short notice (called on a worker thread).</param>
    public SpeechOutput(Func<AppSettings> settings, ISecretStore secrets, Action<string> toast, Action<string> log)
    {
        _settings = settings;
        _secrets = secrets;
        _toast = toast;
        _log = log;
    }

    /// <summary>Read replies aloud (AppSettings.SpeakReplies). Previews and the test sound play regardless.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A word of a reply as playback reaches it (previews raise none). Raised on a worker thread.</summary>
    public event Action<string>? WordSpoken;

    /// <summary>After each <see cref="SpeakAsync"/> finishes, fails or is stopped. Raised on a worker thread.</summary>
    public event Action? SpeechEnded;

    /// <summary>A reply (not a preview) is about to be spoken. Raised on the caller's thread; handlers must be quick.</summary>
    public event Action? ReplySpeechStarting;

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        RenderDevices.RaiseEach(ReplySpeechStarting, h => h(), _log, "speech_starting");
        return SpeakCoreAsync(text, null, cancellationToken);
    }

    /// <summary>
    /// Says a job announcement. It plays even with <see cref="Enabled"/> (Settings → Voice "read replies aloud") off, since the
    /// announcement toggle is its own, but then only in a local Windows voice: an OpenAI voice would send the line to OpenAI
    /// (<see cref="VoiceCatalog.ForAnnouncement"/>). It is not a reply, so no word reaches the captions bar and no speaker check runs.
    /// </summary>
    public Task AnnounceAsync(string text, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(text) ? Task.CompletedTask
            : SpeakCoreAsync(text, VoiceCatalog.ForAnnouncement(_settings().VoiceId, Enabled), cancellationToken, captions: false);

    /// <summary>
    /// Says the preview line with <paramref name="voiceId"/> at the current speed. Like the test sound, it stops whatever is
    /// playing first: a reply's current sentence and every sentence of it still waiting to play are dropped.
    /// </summary>
    public async Task PreviewAsync(string voiceId)
    {
        Stop();
        try { await SpeakCoreAsync(PreviewText, voiceId, CancellationToken.None).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _log("speech_preview_error=" + ex.GetType().Name); _toast("That voice could not play."); }
    }

    /// <summary>A 0.3 s 880 Hz tone on the selected output device; stops whatever is playing first.</summary>
    public async Task PlayTestSoundAsync()
    {
        Stop();
        var token = StopToken();
        try
        {
            await _playing.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var device = RenderDevices.WaveOutNumber(_settings().OutputDeviceId);
                _log($"speech_test_sound_start device={device}");
                var tone = new SignalGenerator(44_100, 1) { Type = SignalGeneratorType.Sin, Frequency = 880, Gain = 0.25 }.Take(TimeSpan.FromSeconds(0.3));
                await RenderDevices.PlayAsync(tone, device, token).ConfigureAwait(false);
                _log("speech_test_sound_end");
            }
            finally { _playing.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _log("speech_test_sound_error=" + ex.GetType().Name); _toast("The test sound could not play on this device."); }
    }

    /// <summary>A reply, preview or test sound is playing or waiting for the one before it.</summary>
    public bool IsSpeaking => _playing.CurrentCount == 0;

    /// <summary>Cuts playback now; a sentence waiting for its gap or being synthesized is dropped.</summary>
    public void Stop()
    {
        CancellationTokenSource old;
        lock (_gate) (old, _stop) = (_stop, new CancellationTokenSource());
        old.Cancel(); // outside the lock: cancellation callbacks (WaveOut stop, SAPI cancel) run synchronously here
        // ponytail: the old source is not disposed (no timer, nothing to leak)
    }

    private CancellationToken StopToken() { lock (_gate) return _stop.Token; }

    private async Task SpeakCoreAsync(string text, string? voiceOverride, CancellationToken cancellationToken, bool captions = true)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopToken());
        var token = linked.Token;
        try
        {
            await _playing.WaitAsync(token).ConfigureAwait(false);
            try
            {
                foreach (var sentence in SpeechPlan.Sentences(text))
                {
                    var settings = _settings();
                    var (clip, voice) = await RenderAsync(sentence, voiceOverride ?? settings.VoiceId, SettingsCatalog.SnapSpeed(settings.SpeechSpeed), token).ConfigureAwait(false);
                    // Rendered first, so rendering time counts towards the 0.35 s gap; only the rest is waited.
                    var last = Interlocked.Read(ref _lastEndTicks);
                    var gap = SpeechPlan.GapBefore(last == 0 ? null : TimeSpan.FromMilliseconds(Environment.TickCount64 - last));
                    if (gap > TimeSpan.Zero) await Task.Delay(gap, token).ConfigureAwait(false);
                    var device = RenderDevices.WaveOutNumber(settings.OutputDeviceId);
                    _log($"speech_play_start voice={voice} device={device} audio_ms={(int)clip.Info.Duration.TotalMilliseconds}");
                    try { await RenderDevices.PlayAsync(clip, device, voiceOverride is null && captions ? WordSpoken : null, token).ConfigureAwait(false); }
                    finally { Interlocked.Exchange(ref _lastEndTicks, Environment.TickCount64); }
                    _log("speech_play_end");
                }
            }
            finally { _playing.Release(); }
        }
        finally
        {
            // A throwing subscriber must not replace the playback outcome (completion, cancellation or the real error).
            RenderDevices.RaiseEach(SpeechEnded, h => h(), _log, "speech_ended");
        }
    }

    /// <summary>The sentence as audio and which engine made it ("openai" or "windows").</summary>
    private async Task<(SpeechClip, string)> RenderAsync(string sentence, string voiceId, double speed, CancellationToken token)
    {
        if (VoiceCatalog.IsOpenAi(voiceId))
        {
            var key = _secrets.Get(SecretNames.OpenAiApiKey);
            var keyId = StringComparer.Ordinal.GetHashCode(key ?? "");
            if (string.IsNullOrWhiteSpace(key)) Fallback("speech_openai_error=NoKey");
            else if (!_openAi.ShouldTry(keyId, Environment.TickCount64)) _log("speech_openai_skipped"); // a recent failure; already toasted
            else
            {
                try { return (await OpenAiVoice.RenderAsync(key, voiceId, sentence, speed, token).ConfigureAwait(false), "openai"); }
                catch (Exception ex) when (ex is not OutOfMemoryException && !token.IsCancellationRequested)
                {
                    _openAi.Failed(keyId, ex is HttpRequestException { StatusCode: { } status } ? (int)status : null, Environment.TickCount64);
                    Fallback("speech_openai_error=" + ex.GetType().Name); // never the message: it could echo the request
                }
            }
            voiceId = SettingsCatalog.WindowsDefaultVoice;
        }
        // A worker thread: SAPI must not post its events to the UI thread, and synthesis must not block it.
        return (await Task.Run(() => WindowsVoice.RenderAsync(sentence, voiceId, speed, token), token).ConfigureAwait(false), "windows");

        void Fallback(string log)
        {
            _log(log);
            if (Interlocked.Exchange(ref _fallbackToastShown, 1) == 0) _toast("Couldn't reach the OpenAI voice — using a Windows voice instead.");
        }
    }
}

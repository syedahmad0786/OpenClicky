namespace OpenClicky.Core;

/// <summary>
/// Phase A silent spike: PTT → overlay → capture JPEG in memory → log size. No network.
/// </summary>
public sealed class SpikeOrchestrator : IDisposable
{
    private readonly IPushToTalkMonitor _hotkey;
    private readonly IOverlayController _overlay;
    private readonly IScreenCaptureService _capture;
    private readonly Action<string> _log;
    private CancellationTokenSource? _captureCts;
    private CompanionVoiceState _state = CompanionVoiceState.Idle;

    public SpikeOrchestrator(
        IPushToTalkMonitor hotkey,
        IOverlayController overlay,
        IScreenCaptureService capture,
        Action<string>? log = null)
    {
        _hotkey = hotkey;
        _overlay = overlay;
        _capture = capture;
        _log = log ?? Console.WriteLine;
    }

    public CompanionVoiceState State => _state;

    public long LastCaptureBytes { get; private set; }

    public void Start()
    {
        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        _hotkey.Start();
        _log($"openclicky spike started — hold {_hotkey.Chord.DisplayName} (no API keys)");
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        _captureCts?.Cancel();
        _state = CompanionVoiceState.Listening;
        _overlay.ShowListening();
        _log("ptt_pressed → listening");
    }

    private async void OnReleased(object? sender, EventArgs e)
    {
        _state = CompanionVoiceState.Processing;
        _overlay.ShowProcessing();
        _log("ptt_released → capturing");

        _captureCts?.Cancel();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        try
        {
            var captures = await _capture.CaptureAllMonitorsJpegAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var primary = captures.FirstOrDefault(c => c.IsCursorMonitor) ?? captures.FirstOrDefault();
            if (primary is null)
            {
                _log("capture_bytes=0 error=no_monitors");
            }
            else
            {
                LastCaptureBytes = primary.JpegBytes.LongLength;
                var magic = primary.LooksLikeJpeg ? "FFD8" : "????";
                _log($"capture_bytes={primary.JpegBytes.Length} magic={magic} " +
                     $"size={primary.PixelWidth}x{primary.PixelHeight} label={primary.Label}");
            }
        }
        catch (OperationCanceledException)
        {
            _log("capture cancelled");
        }
        catch (Exception ex)
        {
            _log($"capture_error={ex.GetType().Name}: {ex.Message}");
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
        _captureCts?.Cancel();
        _captureCts?.Dispose();
        _hotkey.Dispose();
        _overlay.Dispose();
    }
}

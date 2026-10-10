using OpenClicky.Capture;
using OpenClicky.Core;
using OpenClicky.Input;
using OpenClicky.Overlay;

namespace OpenClicky.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly AppSettings _settings = new();
    private readonly PushToTalkHotkeyMonitor _hotkey;
    private readonly BuddyOverlayController _overlay;
    private readonly SpikeOrchestrator _spike;
    private readonly SettingsForm _settingsForm;

    public TrayApplicationContext()
    {
        _hotkey = new PushToTalkHotkeyMonitor(_settings.PushToTalk);
        _overlay = new BuddyOverlayController();
        var capture = new WgcScreenCaptureService();
        _spike = new SpikeOrchestrator(_hotkey, _overlay, capture, Log);
        _settingsForm = new SettingsForm(_settings, OnHotkeyChanged);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "OpenClicky (Phase A spike)",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Test capture once", null, async (_, _) => await TestCaptureAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowSettings();

        _spike.Start();
        Log($"tray ready — hold {_settings.PushToTalk.DisplayName}");
    }

    private void OnHotkeyChanged(HotkeyChord chord)
    {
        _settings.PushToTalk = chord;
        _hotkey.Chord = chord;
        Log($"hotkey_changed={chord.DisplayName}");
    }

    private void ShowSettings()
    {
        if (_settingsForm.Visible)
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm.Show();
    }

    private async Task TestCaptureAsync()
    {
        try
        {
            var capture = new GdiScreenCaptureService();
            var results = await capture.CaptureAllMonitorsJpegAsync();
            var primary = results.FirstOrDefault(r => r.IsCursorMonitor) ?? results.First();
            Log($"manual_capture_bytes={primary.JpegBytes.Length} magic={(primary.LooksLikeJpeg ? "FFD8" : "????")}");
            MessageBox.Show(
                $"Captured {primary.JpegBytes.Length} bytes ({primary.PixelWidth}x{primary.PixelHeight}).\nJPEG magic OK: {primary.LooksLikeJpeg}",
                "OpenClicky capture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"manual_capture_error={ex.Message}");
            MessageBox.Show(ex.Message, "OpenClicky capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Log(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        DebugLog.Append(line);
        System.Diagnostics.Debug.WriteLine(line);
    }

    protected override void ExitThreadCore()
    {
        _spike.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm.Dispose();
        base.ExitThreadCore();
    }
}

internal static class DebugLog
{
    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();

    public static void Append(string line)
    {
        lock (Gate)
        {
            Lines.Add(line);
            if (Lines.Count > 200)
            {
                Lines.RemoveAt(0);
            }
        }
    }

    public static string Snapshot()
    {
        lock (Gate)
        {
            return string.Join(Environment.NewLine, Lines);
        }
    }
}

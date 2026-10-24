using System.Speech.Synthesis;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Providers;
using AudioState = NAudio.CoreAudioApi.DeviceState;
using InstalledVoice = OpenClicky.Core.Settings.InstalledVoice;
using Device = OpenClicky.Core.Shell.Device;
using DeviceState = OpenClicky.Core.Shell.DeviceState;

namespace OpenClicky.App.Settings;

/// <summary>
/// What the settings page needs from the machine (audio devices, Windows voices, ChatGPT sign-in and models, Whisper,
/// Chrome, privacy consent). Probed on a worker thread and cached: <see cref="Current"/> never does I/O, so building the
/// shell state never blocks the UI thread. Refreshed on request (settings sections, actions) and on audio device changes.
/// Routines and connectors are not probed here; the host already reads them for each push.
/// </summary>
internal sealed class SettingsRuntimeProbe : IDisposable
{
    private readonly ISecretStore _secrets;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _chromeConnected;
    private readonly string _defaultAgentFolder;
    private readonly Action _changed;
    private readonly object _gate = new();
    private readonly MMDeviceEnumerator? _devices;
    private readonly DeviceWatcher? _watcher;
    private SettingsRuntime _cached = SettingsRuntime.Empty;
    private IReadOnlyDictionary<string, string> _voiceNames = new Dictionary<string, string>();
    private double? _whisperDownload;
    private bool _busy, _dirty, _disposed;

    /// <param name="changed">Raised on a worker thread after new facts were cached.</param>
    public SettingsRuntimeProbe(ISecretStore secrets, Func<AppSettings> settings, Func<bool> chromeConnected, string defaultAgentFolder, Action changed)
    {
        _secrets = secrets;
        _settings = settings;
        _chromeConnected = chromeConnected;
        _defaultAgentFolder = defaultAgentFolder;
        _changed = changed;
        try
        {
            _devices = new MMDeviceEnumerator();
            _watcher = new DeviceWatcher(Refresh);
            _devices.RegisterEndpointNotificationCallback(_watcher);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            DebugLog.Append("settings_probe_device_watch_error=" + ex.GetType().Name);
        }
    }

    public SettingsRuntime Current
    {
        get
        {
            lock (_gate)
            {
                var settings = _settings();
                return _cached with
                {
                    Models = NativeSettings.CachedModels.Select(m => m.Slug).ToList(),
                    OpenRouterKeySet = !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey),
                    Whisper = _whisperDownload is null ? _cached.Whisper : WhisperState.Downloading,
                    WhisperProgress = _whisperDownload ?? (_cached.Whisper == WhisperState.Ready ? 1 : 0),
                };
            }
        }
    }

    /// <summary>The Windows voice name behind a short id from <see cref="SettingsActionArgs.VoiceShortId"/>; null when not installed.</summary>
    public string? VoiceName(string id)
    {
        lock (_gate) return _voiceNames.GetValueOrDefault(id);
    }

    /// <summary>Whisper download progress 0–1, or null when no download is running (the next refresh reads the disk again).</summary>
    public void SetWhisperDownload(double? progress)
    {
        lock (_gate) _whisperDownload = progress;
    }

    /// <summary>Starts a probe on a worker thread; a request while one runs queues exactly one more.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_busy) { _dirty = true; return; }
            _busy = true;
        }
        _ = Task.Run(() =>
        {
            var finished = false;
            try
            {
                while (true)
                {
                    var (runtime, voices) = Probe();
                    lock (_gate)
                    {
                        _cached = runtime;
                        _voiceNames = voices;
                        if (!_dirty || _disposed) { _busy = false; finished = true; break; }
                        _dirty = false;
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DebugLog.Append("settings_probe_error=" + ex.GetType().Name);
            }
            finally
            {
                // A probe that threw must not leave the flag set: every later Refresh would only queue.
                if (!finished) lock (_gate) { _busy = false; _dirty = false; }
            }
            bool disposed;
            lock (_gate) disposed = _disposed;
            if (finished && !disposed) _changed();
        });
    }

    private (SettingsRuntime, IReadOnlyDictionary<string, string>) Probe()
    {
        var voices = new Dictionary<string, string>(StringComparer.Ordinal);
        var installed = Guard("voices", () =>
        {
            using var synth = new SpeechSynthesizer();
            var list = new List<InstalledVoice>();
            foreach (var voice in synth.GetInstalledVoices().Where(v => v.Enabled))
            {
                var id = SettingsActionArgs.VoiceShortId(voice.VoiceInfo.Id);
                if (voices.TryAdd(id, voice.VoiceInfo.Name)) list.Add(new InstalledVoice(id, voice.VoiceInfo.Name));
            }
            return (IReadOnlyList<InstalledVoice>)list;
        }, []);
        var credential = Guard("chatgpt", () => ChatGptCredentialStore.Load(_secrets), null);
        var runtime = new SettingsRuntime
        {
            CaptureDevices = Guard("capture devices", () => Devices(DataFlow.Capture), []),
            RenderDevices = Guard("render devices", () => Devices(DataFlow.Render), []),
            InstalledVoices = installed,
            SignedInAs = credential?.AccessToken is { Length: > 0 } ? credential.Email ?? _settings().ChatGptAccountEmail ?? "your ChatGPT account" : null,
            Whisper = Guard("whisper", () => LocalWhisperModel.IsAvailable, false) ? WhisperState.Ready : WhisperState.Missing,
            Chrome = Guard("chrome", () => !File.Exists(ChromeBridgeConfiguration.DefaultPath) ? ChromeState.NotInstalled
                : _chromeConnected() ? ChromeState.Connected : ChromeState.Waiting, ChromeState.NotInstalled),
            Reach = Guard("privacy", PrivacyConsent.Read, []),
            DefaultAgentFolder = _defaultAgentFolder,
            OpenAiKeySet = Guard("openai key", () => !string.IsNullOrWhiteSpace(_secrets.Get(SecretNames.OpenAiApiKey)), false),
        };
        return (runtime, voices);
    }

    /// <summary>Active and unplugged endpoints (unplugged ones are listed unavailable so a saved choice stays visible).</summary>
    private static IReadOnlyList<Device> Devices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var list = new List<Device>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, AudioState.Active | AudioState.Unplugged))
            using (device)
            {
                try { list.Add(new Device(device.ID, device.FriendlyName, device.State == AudioState.Active ? DeviceState.Active : DeviceState.Unavailable)); }
                catch (System.Runtime.InteropServices.COMException) { } // an endpoint vanished mid-enumeration
            }
        return list;
    }

    private static T Guard<T>(string name, Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            DebugLog.Append($"settings_probe_{name.Replace(' ', '_')}_error={ex.GetType().Name}");
            return fallback;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        try { if (_watcher is not null) _devices?.UnregisterEndpointNotificationCallback(_watcher); }
        catch (System.Runtime.InteropServices.COMException) { }
        _devices?.Dispose();
    }

    /// <summary>Audio endpoint changes (plugged, unplugged, new default). Property changes are frequent and ignored.</summary>
    private sealed class DeviceWatcher(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, AudioState newState) => changed();
        public void OnDeviceAdded(string pwstrDeviceId) => changed();
        public void OnDeviceRemoved(string deviceId) => changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => changed();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}

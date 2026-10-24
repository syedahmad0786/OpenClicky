using System.Diagnostics;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>Explicit, guided preparation for the optional unpacked Chrome bridge.</summary>
internal sealed class ChromeBridgeSetupForm : Form
{
    private readonly TextBox _hostPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _origins = new() { Dock = DockStyle.Fill, Multiline = true, Height = 90, PlaceholderText = "https://example.com/" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button _prepare = new() { Text = "Prepare local bridge", AutoSize = true };

    private readonly string _extensionDirectory;

    public ChromeBridgeSetupForm(string? nativeHostPath = null, string? extensionDirectory = null)
    {
        _extensionDirectory = extensionDirectory ?? DeploymentPathResolver.ResolveDirectory("chrome-extension");
        Text = "Set up optional Chrome bridge";
        Width = 660; Height = 440; StartPosition = FormStartPosition.CenterParent;
        _hostPath.Text = nativeHostPath ?? Application.ExecutablePath;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new(600, 0),
            Text = "This optional unpacked extension can read visible controls and act in existing signed-in tabs only on the exact origins you enter. Page titles, visible text, typed text, and approved actions pass locally through Chrome native messaging to OpenClicky."
        });
        layout.Controls.Add(new Label { Text = "Published OpenClicky executable", AutoSize = true, Padding = new(0, 12, 0, 2) });
        var hostRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        hostRow.ColumnStyles.Add(new(SizeType.Percent, 100)); hostRow.ColumnStyles.Add(new(SizeType.AutoSize));
        hostRow.Controls.Add(_hostPath);
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseHost();
        hostRow.Controls.Add(browse);
        layout.Controls.Add(hostRow);
        layout.Controls.Add(new Label { Text = "Allowed origins, one per line (exact HTTP(S) origins ending in /)", AutoSize = true, Padding = new(0, 12, 0, 2) });
        layout.Controls.Add(_origins);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new(600, 0),
            Padding = new(0, 12, 0, 8),
            Text = "Preparing writes files under LocalAppData and registers a per-user Chrome native host. It does not install or enable the extension. Chrome will show the requested site access when you load it unpacked."
        });
        _prepare.Click += async (_, _) => await PrepareAsync();
        layout.Controls.Add(_prepare);
        layout.Controls.Add(_status);
        Controls.Add(layout);
    }

    private void BrowseHost()
    {
        using var dialog = new OpenFileDialog { Filter = "OpenClicky executable (*.exe)|*.exe", CheckFileExists = true, FileName = _hostPath.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _hostPath.Text = dialog.FileName;
    }

    private async Task PrepareAsync()
    {
        var script = Path.Combine(_extensionDirectory, "setup-local.ps1");
        var origins = _origins.Lines.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!File.Exists(script) || !File.Exists(_hostPath.Text) || origins.Length == 0 || origins.Any(origin => !ExactOrigin(origin)))
        {
            _status.Text = "Choose the published executable and enter at least one exact HTTP(S) origin ending in /.";
            return;
        }

        _prepare.Enabled = false;
        _status.Text = "Preparing local files and per-user native-host registration…";
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File"); start.ArgumentList.Add(script);
            start.ArgumentList.Add("-NativeHostPath"); start.ArgumentList.Add(Path.GetFullPath(_hostPath.Text));
            start.ArgumentList.Add("-AllowedOrigins"); start.ArgumentList.Add(string.Join(",", origins));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell could not start.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Setup script failed. No Chrome extension was enabled.");
            var extensionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "ChromeBridge");
            _status.Text = $"Prepared. In chrome://extensions, enable Developer mode, choose Load unpacked, and select:\n{extensionPath}";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _prepare.Enabled = true; }
    }

    private static bool ExactOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.PathAndQuery == "/" && uri.Fragment.Length == 0;
}

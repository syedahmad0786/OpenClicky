using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using OpenClicky.Agent;
using OpenClicky.Core;

namespace OpenClicky.Agent.Smoke;

// Windows CI / local proof: launch OpenClicky.Sandbox, drive confirm-gated Agent
// click + type (auto-confirm ONLY in this harness), assert the sandbox UI changed.
// Does NOT exercise tray, PTT, overlay, STT, vision, or TTS.
internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--harvest-fixture") return HarvestFixture(args[1]);
        if (args.Contains("--harvest", StringComparer.OrdinalIgnoreCase)) return await RunHarvestAsync().ConfigureAwait(true);
        if (args.Contains("--snap", StringComparer.OrdinalIgnoreCase)) return await RunSnapAsync().ConfigureAwait(true);
        Console.WriteLine("OpenClicky agent↔sandbox smoke starting…");

        var sandboxExe = ResolveSandboxExe(args);
        if (sandboxExe is null)
        {
            Console.Error.WriteLine("FAIL: could not locate OpenClicky.Sandbox.exe (pass path as argv[0] or set OPENCLICKY_SANDBOX_EXE)");
            return 1;
        }

        Console.WriteLine($"sandbox_exe={sandboxExe}");

        Process? sandbox = null;
        try
        {
            sandbox = Process.Start(new ProcessStartInfo
            {
                FileName = sandboxExe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(sandboxExe)!,
            });
            if (sandbox is null)
            {
                Console.Error.WriteLine("FAIL: Process.Start returned null");
                return 2;
            }

            var window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(30)).ConfigureAwait(true);
            if (window is null)
            {
                Console.Error.WriteLine("FAIL: sandbox window not found");
                return 3;
            }

            if (!ForegroundFocusRestorer.Restore(new IntPtr(window.Current.NativeWindowHandle)))
                throw new InvalidOperationException("Could not bring the launched sandbox to the foreground.");
            await Task.Delay(400).ConfigureAwait(true);

            if (args.Contains("--dictation", StringComparer.OrdinalIgnoreCase))
                return await RunDictationAsync(window).ConfigureAwait(true);

            var confirm = new AutoConfirm();
            var executor = new CompositeAgentExecutor();

            // Prove the confirm gate rejects mutating tools without confirmation.
            var denied = await executor.ExecuteAsync(
                new AgentActionRequest(
                    AgentToolNames.Click,
                    new Dictionary<string, string> { ["x"] = "0", ["y"] = "0" },
                    "gate check"),
                userConfirmed: false).ConfigureAwait(true);
            if (denied.Success || !denied.RequiresConfirmation)
            {
                Console.Error.WriteLine(
                    $"FAIL: expected confirmation gate; got success={denied.Success} requiresConfirm={denied.RequiresConfirmation} msg={denied.Message}");
                return 4;
            }

            Console.WriteLine("confirm_gate=ok");

            var button = FindDescendant(window, ControlType.Button, "Green button");
            if (button is null)
            {
                Console.Error.WriteLine("FAIL: Green button not found via UIA");
                return 5;
            }

            var clickPoint = button.GetClickablePoint();
            var x = (int)Math.Round(clickPoint.X);
            var y = (int)Math.Round(clickPoint.Y);
            Console.WriteLine($"click_target=({x},{y})");

            var clickResult = await ConfirmGatedRunner.RunAsync(
                confirm,
                executor,
                new AgentActionRequest(
                    AgentToolNames.Click,
                    new Dictionary<string, string>
                    {
                        ["x"] = x.ToString(),
                        ["y"] = y.ToString(),
                        ["space"] = "virtual",
                    },
                    "click green button"),
                CancellationToken.None).ConfigureAwait(true);

            if (!clickResult.Success)
            {
                Console.Error.WriteLine($"FAIL: click failed: {clickResult.Message}");
                return 6;
            }

            if (!await WaitUntilAsync(
                    () => StatusTextContains(window, "clicked"),
                    TimeSpan.FromSeconds(8)).ConfigureAwait(true))
            {
                Console.Error.WriteLine("FAIL: sandbox status did not show a click");
                DumpUia(window);
                return 7;
            }

            Console.WriteLine("click_assert=ok");

            // Re-resolve the window/edit — UIA peers can go stale after the first click.
            window = await WaitForSandboxWindowAsync(sandbox.Id, TimeSpan.FromSeconds(5)).ConfigureAwait(true)
                     ?? window;
            if (!ForegroundFocusRestorer.Restore(new IntPtr(window.Current.NativeWindowHandle)))
                throw new InvalidOperationException("Could not restore the launched sandbox.");

            var edit = FindDescendant(window, ControlType.Edit, "Type target")
                       ?? FindFirstEdit(window);
            if (edit is null)
            {
                Console.Error.WriteLine("FAIL: Type target edit not found via UIA");
                return 8;
            }

            // Focus via real click (same path as agents) — UIA SetFocus alone is flaky for SendInput on CI.
            var editPoint = edit.GetClickablePoint();
            var editX = (int)Math.Round(editPoint.X);
            var editY = (int)Math.Round(editPoint.Y);
            Console.WriteLine($"edit_click_target=({editX},{editY})");

            var focusClick = await ConfirmGatedRunner.RunAsync(
                confirm,
                executor,
                new AgentActionRequest(
                    AgentToolNames.Click,
                    new Dictionary<string, string>
                    {
                        ["x"] = editX.ToString(),
                        ["y"] = editY.ToString(),
                        ["space"] = "virtual",
                    },
                    "focus type target"),
                CancellationToken.None).ConfigureAwait(true);
            if (!focusClick.Success)
            {
                Console.Error.WriteLine($"FAIL: focus click failed: {focusClick.Message}");
                return 8;
            }

            await Task.Delay(300).ConfigureAwait(true);
            try
            {
                edit.SetFocus();
            }
            catch
            {
                // optional; mouse click is the primary focus path
            }

            await Task.Delay(200).ConfigureAwait(true);

            const string typed = "hello";
            var typeResult = await ConfirmGatedRunner.RunAsync(
                confirm,
                executor,
                new AgentActionRequest(
                    AgentToolNames.TypeText,
                    new Dictionary<string, string> { ["text"] = typed },
                    "type into sandbox field"),
                CancellationToken.None).ConfigureAwait(true);

            if (!typeResult.Success)
            {
                Console.Error.WriteLine($"FAIL: type_text failed: {typeResult.Message}");
                return 9;
            }

            // Refresh edit peer before reading value.
            edit = FindDescendant(window, ControlType.Edit, "Type target")
                   ?? FindFirstEdit(window)
                   ?? edit;

            if (!await WaitUntilAsync(
                    () => ReadEditValue(edit) == typed,
                    TimeSpan.FromSeconds(8)).ConfigureAwait(true))
            {
                Console.Error.WriteLine($"FAIL: sandbox edit value did not become '{typed}' (got '{ReadEditValue(edit)}')");
                DumpUia(window);
                return 10;
            }

            Console.WriteLine("type_assert=ok");

            // click green + focus edit + type_text
            if (confirm.ConfirmCalls < 3)
            {
                Console.Error.WriteLine($"FAIL: expected >=3 auto-confirm calls, got {confirm.ConfirmCalls}");
                return 11;
            }

            Console.WriteLine($"PASS confirms={confirm.ConfirmCalls} click=ok type={typed}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex}");
            return 99;
        }
        finally
        {
            if (sandbox is { HasExited: false })
            {
                try
                {
                    sandbox.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort cleanup
                }
            }

            sandbox?.Dispose();
        }
    }

    private static async Task<int> RunDictationAsync(AutomationElement window)
    {
        var edit = FindDescendant(window, ControlType.Edit, "Type target") ?? FindFirstEdit(window);
        if (edit is null) { Console.Error.WriteLine("FAIL: dictation target edit not found"); return 20; }

        var point = edit.GetClickablePoint();
        var executor = new CompositeAgentExecutor();
        var focus = await executor.ExecuteAsync(new AgentActionRequest(
            AgentToolNames.Click,
            new Dictionary<string, string>
            {
                ["x"] = ((int)Math.Round(point.X)).ToString(),
                ["y"] = ((int)Math.Round(point.Y)).ToString(),
            },
            "focus deterministic dictation target"), userConfirmed: true).ConfigureAwait(true);
        if (!focus.Success) { Console.Error.WriteLine("FAIL: " + focus.Message); return 21; }
        await Task.Delay(250).ConfigureAwait(true);

        if (!DictationTarget.TryCapture(out var target, out var error) || target is null)
        { Console.Error.WriteLine("FAIL: dictation target capture: " + error); return 22; }

        const string transcript = "deterministic local dictation draft";
        var draft = new DictationDraft();
        draft.AcceptTranscript(transcript);
        var result = await target.InsertAsync(draft.Draft, executor).ConfigureAwait(true);
        Console.WriteLine($"dictation_result attempted={result.InputAttempted} completed={result.InputCompleted} verified={result.Verified} message={result.Message}");
        if (!result.InputCompleted || !result.Verified || result.FinalText != transcript)
        {
            Console.Error.WriteLine($"FAIL: expected verified exact text '{transcript}', got '{result.FinalText}'");
            return 23;
        }

        Console.WriteLine($"PASS dictation target={target.ControlName} raw={draft.RawTranscript} final={result.FinalText}");
        return 0;
    }

    private const string PlainText = "harvest fixture plain words", SecretText = "harvest-fixture-secret";

    /// <summary>
    /// A fixture window of its own process (never activated): a plain text box, a password box and a button. Prints each one's
    /// centre and then its screen bounds ("name cx cy x y w h").
    /// </summary>
    private static int HarvestFixture(string title)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // physical pixels, as OpenClicky reads them
        var area = Screen.PrimaryScreen!.WorkingArea;
        using var form = new FixtureForm
        {
            Text = title, TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(area.Left + 60, area.Top + 60), Size = new Size(460, 260),
        };
        var plain = new TextBox { Text = PlainText, Location = new Point(20, 20), Width = 380 };
        var secret = new TextBox { Text = SecretText, UseSystemPasswordChar = true, Location = new Point(20, 80), Width = 380 };
        var button = new Button { Text = "Fixture button", Location = new Point(20, 140), Size = new Size(160, 36) };
        form.Controls.AddRange([plain, secret, button]);
        static string Where(Control c)
        {
            var p = c.PointToScreen(new Point(c.Width / 2, c.Height / 2));
            var r = c.Parent!.RectangleToScreen(c.Bounds); // the whole control, border included, as UIA reports it
            return $"{p.X} {p.Y} {r.X} {r.Y} {r.Width} {r.Height}";
        }
        form.Shown += (_, _) =>
        {
            Console.WriteLine("plain " + Where(plain));
            Console.WriteLine("secret " + Where(secret));
            Console.WriteLine("button " + Where(button));
            Console.Out.Flush();
        };
        Application.Run(form);
        return 0;
    }

    private sealed class FixtureForm : Form
    {
        protected override bool ShowWithoutActivation => true; // never takes focus from the user
    }

    /// <summary>
    /// The highlight harvester's refusals, against fixture windows this smoke creates in another process: a plain box is read
    /// (the control), a password box is not, and a window whose title is on the deny list gives nothing. It only reads.
    /// </summary>
    private static async Task<int> RunHarvestAsync()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        foreach (var (title, denied) in new[] { ("OpenClicky harvest fixture", false), ("1Password harvest fixture", true) })
        {
            using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--harvest-fixture", title])
            { UseShellExecute = false, RedirectStandardOutput = true })!;
            try
            {
                if (await ReadFixtureAsync(fixture).ConfigureAwait(true) is not { } layout)
                { Console.Error.WriteLine($"FAIL: harvest fixture '{title}' did not report its boxes"); return 30; }
                var points = layout.ToDictionary(e => e.Key, e => new OpenClicky.Core.Drawing.Pt(e.Value[0], e.Value[1]));

                var plain = UiaTextHarvester.Harvest([points["plain"]], Environment.ProcessId, TimeSpan.FromSeconds(2));
                var secret = UiaTextHarvester.Harvest([points["secret"]], Environment.ProcessId, TimeSpan.FromSeconds(2));
                Console.WriteLine($"harvest title='{title}' plain=[{string.Join(" | ", plain.Select(t => t.Text))}] secret_items={secret.Count}");
                if (secret.Any(t => t.Text.Contains(SecretText, StringComparison.Ordinal)) || secret.Count != 0)
                { Console.Error.WriteLine("FAIL: the harvester read a password box"); return 31; }
                if (denied && plain.Count != 0)
                { Console.Error.WriteLine("FAIL: the harvester read a window with a deny-listed title"); return 32; }
                if (!denied && !plain.Any(t => t.Text.Contains(PlainText, StringComparison.Ordinal)))
                { Console.Error.WriteLine("FAIL: the harvester did not read the plain box (the control check)"); return 33; }
            }
            finally
            {
                try { fixture.Kill(); fixture.WaitForExit(5000); } catch (InvalidOperationException) { }
            }
        }
        Console.WriteLine("PASS: harvester reads a plain box, never a password box or a deny-listed window");
        return 0;
    }

    /// <summary>The fixture's "name cx cy x y w h" lines once all three controls are reported and painted, or null after 20 s.</summary>
    private static async Task<Dictionary<string, float[]>?> ReadFixtureAsync(Process fixture)
    {
        var layout = new Dictionary<string, float[]>();
        var reading = Task.Run(async () =>
        {
            while (layout.Count < 3 && await fixture.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split(' ');
                layout[parts[0]] = parts[1..].Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            }
        });
        if (await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(true) != reading || layout.Count < 3) return null;
        await Task.Delay(500).ConfigureAwait(true); // painted and on top
        return layout;
    }

    /// <summary>
    /// The POINT snapper against the same fixture in another process: the button and the text box snap to their own bounds, the
    /// password box and a deny-listed window snap to nothing, and so does a window of this process. It only reads.
    /// </summary>
    private static async Task<int> RunSnapAsync()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        static string Show(OpenClicky.Core.Drawing.Box? b) => b is { } x ? $"{x.X},{x.Y},{x.W},{x.H}" : "none";
        static bool Same(OpenClicky.Core.Drawing.Box? b, float[] r) => b is { } x && x.X == r[2] && x.Y == r[3] && x.W == r[4] && x.H == r[5];
        foreach (var (title, denied) in new[] { ("OpenClicky snap fixture", false), ("1Password snap fixture", true) })
        {
            using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--harvest-fixture", title])
            { UseShellExecute = false, RedirectStandardOutput = true })!;
            try
            {
                if (await ReadFixtureAsync(fixture).ConfigureAwait(true) is not { } layout)
                { Console.Error.WriteLine($"FAIL: snap fixture '{title}' did not report its controls"); return 40; }
                OpenClicky.Core.Drawing.Box? At(string name) =>
                    UiaControlSnapper.Snap((int)layout[name][0], (int)layout[name][1], TimeSpan.FromSeconds(2));
                var (button, plain, secret) = (At("button"), At("plain"), At("secret"));
                Console.WriteLine($"snap title='{title}' button={Show(button)} plain={Show(plain)} secret={Show(secret)} expected_button={string.Join(',', layout["button"][2..])}");
                if (secret is not null) { Console.Error.WriteLine("FAIL: the snapper read a password box"); return 41; }
                if (denied && (button is not null || plain is not null)) { Console.Error.WriteLine("FAIL: the snapper read a deny-listed window"); return 42; }
                if (!denied && (!Same(button, layout["button"]) || !Same(plain, layout["plain"])))
                { Console.Error.WriteLine("FAIL: the snapper did not fit the button and the text box (the control check)"); return 43; }
            }
            finally
            {
                try { fixture.Kill(); fixture.WaitForExit(5000); } catch (InvalidOperationException) { }
            }
        }

        // A window of this process: refused before UI Automation is asked anything.
        var area = Screen.PrimaryScreen!.WorkingArea;
        using (var own = new FixtureForm { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                   Location = new Point(area.Left + 60, area.Top + 60), Size = new Size(300, 120) })
        {
            var ownButton = new Button { Text = "Own button", Location = new Point(10, 10), Size = new Size(120, 36) };
            own.Controls.Add(ownButton);
            own.Show();
            Application.DoEvents();
            var centre = ownButton.PointToScreen(new Point(60, 18));
            var snapped = UiaControlSnapper.Snap(centre.X, centre.Y, TimeSpan.FromSeconds(1));
            Console.WriteLine($"snap own_window button={Show(snapped)}");
            if (snapped is not null) { Console.Error.WriteLine("FAIL: the snapper read OpenClicky's own window"); return 44; }
        }
        Console.WriteLine("PASS: snapper fits real controls, never a password box, a deny-listed window or its own window");
        return 0;
    }

    private static string? ResolveSandboxExe(string[] args)
    {
        if (args.Length > 0 && File.Exists(args[0]))
        {
            return Path.GetFullPath(args[0]);
        }

        var env = Environment.GetEnvironmentVariable("OPENCLICKY_SANDBOX_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        // Walk up from the smoke output looking for a sibling Sandbox build.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidates = new[]
            {
                Path.Combine(dir.FullName, "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
                Path.Combine(dir.FullName, "apps", "windows", "OpenClicky.Sandbox", "bin", "Release", "net8.0-windows", "OpenClicky.Sandbox.exe"),
                Path.Combine(dir.FullName, "OpenClicky.Sandbox", "bin", "Debug", "net8.0-windows", "OpenClicky.Sandbox.exe"),
                Path.Combine(dir.FullName, "apps", "windows", "OpenClicky.Sandbox", "bin", "Debug", "net8.0-windows", "OpenClicky.Sandbox.exe"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    return c;
                }
            }
        }

        return null;
    }

    private static async Task<AutomationElement?> WaitForSandboxWindowAsync(int processId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var root = AutomationElement.RootElement;
            var window = root.FindFirst(
                TreeScope.Children,
                new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "OpenClicky Agent Sandbox"),
                    new PropertyCondition(AutomationElement.ProcessIdProperty, processId)));
            if (window is not null)
            {
                return window;
            }

            await Task.Delay(200).ConfigureAwait(true);
        }

        return null;
    }

    private static AutomationElement? FindDescendant(AutomationElement root, ControlType type, string name)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, type),
                    new PropertyCondition(AutomationElement.NameProperty, name)));
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? FindFirstEdit(AutomationElement root)
    {
        try
        {
            return root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        }
        catch
        {
            return null;
        }
    }

    private static bool StatusTextContains(AutomationElement window, string fragment)
    {
        try
        {
            var status = FindDescendant(window, ControlType.Text, "Sandbox status")
                         ?? FindDescendant(window, ControlType.Text, "Ready.");
            if (status is not null)
            {
                var name = status.Current.Name ?? string.Empty;
                if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Fallback: any text node mentioning the fragment (status AccessibleName updates with click count).
            var texts = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            foreach (AutomationElement el in texts)
            {
                var name = el.Current.Name ?? string.Empty;
                if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Green button clicked", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // UIA race
        }

        return false;
    }

    private static string? ReadEditValue(AutomationElement edit)
    {
        try
        {
            if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)
                && patternObj is ValuePattern value)
            {
                return value.Current.Value;
            }

            return edit.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(150).ConfigureAwait(true);
        }

        return predicate();
    }

    private static void DumpUia(AutomationElement window)
    {
        try
        {
            Console.Error.WriteLine("--- uia dump ---");
            Console.Error.WriteLine(new UiaSnapshotService().CaptureForeground(40));
            _ = window;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"uia_dump_error={ex.Message}");
        }
    }

}

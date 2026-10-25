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
        if (args.Contains("--integrity", StringComparer.OrdinalIgnoreCase)) return await RunIntegrityAsync().ConfigureAwait(true);
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

    /// <summary>
    /// Own-window recognition against the foreign fixture: an own click-through (layered + transparent) window laid over the
    /// fixture's button is not "ours" (and the snapper still fits the button through it); the fixture's window reparented as a
    /// child of an own form is "ours" although the window under the point belongs to the fixture's process, as WebView2's
    /// renderer child does inside the classic home window. Only window styles and parents change; nothing is clicked or typed.
    /// </summary>
    private static int OwnWindowChecks(Process fixture, string title, float[] button)
    {
        int cx = (int)button[0], cy = (int)button[1];
        using (var glass = new ClickThroughForm { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                   FormBorderStyle = FormBorderStyle.None, Opacity = 0.4, BackColor = Color.Magenta,
                   Location = new Point((int)button[2] - 20, (int)button[3] - 20), Size = new Size((int)button[4] + 40, (int)button[5] + 40) })
        {
            glass.Show();
            Pump(300);
            var covers = glass.Bounds.Contains(cx, cy) && GetAncestor(WindowFromPoint(new NativePoint { X = cx, Y = cy }), 2) != glass.Handle;
            var ours = ForegroundWindowInfo.IsCurrentProcessWindowAt(cx, cy);
            var through = UiaControlSnapper.Snap(cx, cy, TimeSpan.FromSeconds(2));
            Console.WriteLine($"snap click_through_over_fixture covers={covers} ours={ours} button={(through is { } b ? $"{b.X},{b.Y},{b.W},{b.H}" : "none")}");
            if (!covers || ours) { Console.Error.WriteLine("FAIL: an own click-through window counted as an OpenClicky click target"); return 45; }
            // Non-vacuous: without WS_EX_TRANSPARENT the same window is on top at that point and takes the hit.
            const int GwlExStyle = -20;
            SetWindowLongPtr(glass.Handle, GwlExStyle, (IntPtr)(GetWindowLongPtr(glass.Handle, GwlExStyle).ToInt64() & ~0x20L));
            Pump(100);
            var opaqueHit = GetAncestor(WindowFromPoint(new NativePoint { X = cx, Y = cy }), 2) == glass.Handle;
            Console.WriteLine($"snap same_window_without_transparent_takes_hit={opaqueHit}");
            if (!opaqueHit) { Console.Error.WriteLine("FAIL: the click-through window was not on top of the fixture's button"); return 45; }
            if (through is not { } t || t.X != button[2] || t.Y != button[3] || t.W != button[4] || t.H != button[5])
            { Console.Error.WriteLine("FAIL: the snapper did not see the button through an own click-through window"); return 48; }
        }

        var foreign = FindWindow(null, title);
        if (foreign == IntPtr.Zero) { Console.Error.WriteLine("FAIL: fixture window not found"); return 46; }
        var area = Screen.PrimaryScreen!.WorkingArea;
        using var host = new FixtureForm { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
            Location = new Point(area.Left + 60, area.Top + 60), Size = new Size(520, 340) };
        host.Show();
        Pump(100);
        const int GwlStyle = -16;
        const long WsChild = 0x40000000, WsPopup = 0x80000000;
        SetWindowLongPtr(foreign, GwlStyle, (IntPtr)((GetWindowLongPtr(foreign, GwlStyle).ToInt64() & ~WsPopup) | WsChild));
        SetParent(foreign, host.Handle);
        SetWindowPos(foreign, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0020); // NOSIZE NOZORDER NOACTIVATE FRAMECHANGED
        Pump(400);
        GetWindowRect(foreign, out var r);
        int px = (r.Left + r.Right) / 2, py = (r.Top + r.Bottom) / 2;
        var under = WindowFromPoint(new NativePoint { X = px, Y = py });
        var underPid = ForegroundWindowInfo.ProcessIdOf(under);
        var inOwnRoot = ForegroundWindowInfo.IsCurrentProcessWindowAt(px, py);
        Console.WriteLine($"snap foreign_child_in_own_window under_pid={(underPid == fixture.Id ? "fixture" : underPid.ToString())} ours={inOwnRoot}");
        if (underPid != fixture.Id || !inOwnRoot)
        { Console.Error.WriteLine("FAIL: a child another process renders inside an own window was not recognised as OpenClicky's"); return 46; }
        return 0;
    }

    private static void Pump(int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(15); }
    }

    private sealed class ClickThroughForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000; // LAYERED TRANSPARENT TOOLWINDOW NOACTIVATE, as the overlay
                return p;
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

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
    /// Drive's elevation guard reads integrity only: this process, a window of its own, and a fixture process it started (same
    /// integrity) are read alike and allowed; no window reads as unreadable and is refused. Nothing is clicked or typed.
    /// </summary>
    private static async Task<int> RunIntegrityAsync()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var own = ProcessIntegrity.Own();
        Console.WriteLine($"own_integrity=0x{own:X}");
        if (own < 0x1000) { Console.Error.WriteLine("FAIL: own integrity unreadable"); return 50; }
        if (ProcessIntegrity.Of(0) is not null || !OpenClicky.Core.CompanionCursor.DriveGuard.Refuses(ProcessIntegrity.Of(0), own))
        { Console.Error.WriteLine("FAIL: no window must read as unreadable and be refused"); return 52; }
        using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, ["--harvest-fixture", "OpenClicky integrity fixture"])
        { UseShellExecute = false, RedirectStandardOutput = true })!;
        try
        {
            if (await ReadFixtureAsync(fixture).ConfigureAwait(true) is not { } layout) { Console.Error.WriteLine("FAIL: integrity fixture did not start"); return 53; }
            var button = AutomationElement.FromPoint(new System.Windows.Point(layout["button"][0], layout["button"][1]));
            if (button.Current.ProcessId != fixture.Id) { Console.Error.WriteLine("FAIL: the fixture is not on top"); return 55; }
            var theirs = ProcessIntegrity.Of(button.Current.NativeWindowHandle);
            Console.WriteLine($"fixture_integrity=0x{theirs:X}");
            if (theirs != own || OpenClicky.Core.CompanionCursor.DriveGuard.Refuses(theirs, own))
            { Console.Error.WriteLine("FAIL: a same-integrity fixture must be read and allowed"); return 54; }
        }
        finally { try { fixture.Kill(); } catch (InvalidOperationException) { } }
        // Last: creating a Form installs a WinForms synchronization context with no message loop to resume awaits on.
        using (var form = new Form { ShowInTaskbar = false })
        {
            _ = form.Handle;
            if (ProcessIntegrity.Of(form.Handle) != own) { Console.Error.WriteLine("FAIL: own window integrity differs"); return 51; }
        }
        Console.WriteLine("PASS: integrity guard");
        return 0;
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
                if (!denied && OwnWindowChecks(fixture, title, layout["button"]) is var failed and not 0) return failed;
            }
            finally
            {
                try { fixture.Kill(); fixture.WaitForExit(5000); } catch (InvalidOperationException) { }
            }
        }

        // A window of this process: refused before UI Automation is asked anything.
        var area = Screen.PrimaryScreen!.WorkingArea;
        using (var own = new FixtureForm { TopMost = true, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                   Location = new Point(area.Left + 60, area.Top + 60), Size = new Size(460, 260) })
        {
            // Laid out as the fixture was, so the button sits where the fixture's button was just hit.
            var ownButton = new Button { Text = "Own button", Location = new Point(20, 140), Size = new Size(160, 36) };
            own.Controls.Add(ownButton);
            own.Show();
            Pump(300);
            var centre = ownButton.PointToScreen(new Point(80, 18));
            var background = own.PointToScreen(new Point(300, 160));
            // Non-vacuous: both points really are over this window (another app's topmost window there fails the smoke, not passes it).
            bool OnOwn(Point p) => GetAncestor(WindowFromPoint(new NativePoint { X = p.X, Y = p.Y }), 2) == own.Handle;
            if (!OnOwn(centre) || !OnOwn(background))
            { Console.Error.WriteLine("FAIL: the own fixture window is covered by another window at its test points"); return 47; }
            // Non-vacuous: the point really is over this process's window (a child button), which the shared check refuses.
            var refused = UiaTextHarvester.RefusedAt(centre.X, centre.Y, Environment.ProcessId, null);
            var ownChild = ForegroundWindowInfo.IsCurrentProcessWindowAt(centre.X, centre.Y);
            var ownForm = ForegroundWindowInfo.IsCurrentProcessWindowAt(background.X, background.Y);
            var snapped = UiaControlSnapper.Snap(centre.X, centre.Y, TimeSpan.FromSeconds(1));
            Console.WriteLine($"snap own_window refused={refused} own_child={ownChild} own_form={ownForm} button={Show(snapped)}");
            if (!refused || !ownChild || !ownForm) { Console.Error.WriteLine("FAIL: OpenClicky's own form or child control was not recognised as its own"); return 47; }
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

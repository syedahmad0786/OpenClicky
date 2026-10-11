using System.Runtime.InteropServices;
using System.Text;

namespace OpenClicky.Agent;

/// <summary>Win32 SendInput-backed mutating actions (Phase D MVP).</summary>
public sealed class SendInputExecutor : IAgentActionExecutor
{
    public Task<AgentActionResult> ExecuteAsync(
        AgentActionRequest request,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        var mutating = request.ToolName is AgentToolNames.Click or AgentToolNames.TypeText
            or AgentToolNames.Hotkey or AgentToolNames.Scroll;

        if (mutating && !userConfirmed)
        {
            return Task.FromResult(new AgentActionResult(false, $"Confirmation required for {request.ToolName}", true));
        }

        try
        {
            switch (request.ToolName)
            {
                case AgentToolNames.Click:
                    return Task.FromResult(Click(request));
                case AgentToolNames.TypeText:
                    return Task.FromResult(TypeText(request));
                case AgentToolNames.Wait:
                    return Wait(request, cancellationToken);
                case AgentToolNames.Screenshot:
                case AgentToolNames.UiaSnapshot:
                    return Task.FromResult(new AgentActionResult(
                        false,
                        $"{request.ToolName} is handled by the orchestrator capture path, not SendInput.",
                        false));
                default:
                    return Task.FromResult(new AgentActionResult(
                        false,
                        $"Tool '{request.ToolName}' not implemented in SendInputExecutor yet.",
                        false));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new AgentActionResult(false, ex.Message, false));
        }
    }

    private static AgentActionResult Click(AgentActionRequest request)
    {
        if (!request.Arguments.TryGetValue("x", out var xs) || !request.Arguments.TryGetValue("y", out var ys)
            || !int.TryParse(xs, out var x) || !int.TryParse(ys, out var y))
        {
            return new AgentActionResult(false, "click requires integer x,y", false);
        }

        NativeMethods.SetCursorPos(x, y);
        NativeMethods.mouse_event(NativeMethods.MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(NativeMethods.MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        return new AgentActionResult(true, $"clicked ({x},{y})", false);
    }

    private static AgentActionResult TypeText(AgentActionRequest request)
    {
        if (!request.Arguments.TryGetValue("text", out var text) || string.IsNullOrEmpty(text))
        {
            return new AgentActionResult(false, "type_text requires text", false);
        }

        // Unicode typing via KEYEVENTF_UNICODE
        foreach (var ch in text)
        {
            var down = new NativeMethods.Input
            {
                Type = 1,
                U = new NativeMethods.InputUnion
                {
                    Ki = new NativeMethods.KeybdInput
                    {
                        WScan = ch,
                        DwFlags = NativeMethods.KeyeventfUnicode,
                    },
                },
            };
            var up = down;
            up.U.Ki.DwFlags = NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup;
            NativeMethods.SendInput(1, [down], Marshal.SizeOf<NativeMethods.Input>());
            NativeMethods.SendInput(1, [up], Marshal.SizeOf<NativeMethods.Input>());
        }

        return new AgentActionResult(true, $"typed {text.Length} chars", false);
    }

    private static async Task<AgentActionResult> Wait(AgentActionRequest request, CancellationToken cancellationToken)
    {
        var ms = 500;
        if (request.Arguments.TryGetValue("ms", out var raw) && int.TryParse(raw, out var parsed))
        {
            ms = Math.Clamp(parsed, 0, 30_000);
        }

        await Task.Delay(ms, cancellationToken).ConfigureAwait(false);
        return new AgentActionResult(true, $"waited {ms}ms", false);
    }

    private static class NativeMethods
    {
        public const uint MouseeventfLeftdown = 0x0002;
        public const uint MouseeventfLeftup = 0x0004;
        public const uint KeyeventfKeyup = 0x0002;
        public const uint KeyeventfUnicode = 0x0004;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public int Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public KeybdInput Ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeybdInput
        {
            public ushort WVk;
            public ushort WScan;
            public uint DwFlags;
            public uint Time;
            public UIntPtr DwExtraInfo;
        }
    }
}

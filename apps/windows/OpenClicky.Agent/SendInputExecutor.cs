using System.Runtime.InteropServices;

namespace OpenClicky.Agent;

/// <summary>Win32 SendInput-backed mutating actions (Phase D MVP).</summary>
public sealed class SendInputExecutor : IAgentActionExecutor
{
    public Task<AgentActionResult> ExecuteAsync(
        AgentActionRequest request,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
                    return Task.FromResult(TypeText(request, cancellationToken));
                case AgentToolNames.Scroll:
                    return Task.FromResult(Scroll(request));
                case AgentToolNames.Hotkey:
                    return Task.FromResult(Hotkey(request));
                case AgentToolNames.Wait:
                    return Wait(request, cancellationToken);
                case AgentToolNames.Screenshot:
                case AgentToolNames.UiaSnapshot:
                    return Task.FromResult(new AgentActionResult(
                        false,
                        $"{request.ToolName} is handled by the composite executor / orchestrator.",
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

        if (!NativeMethods.SetCursorPos(x, y)) return new AgentActionResult(false, "Could not move to the click target.", false);
        NativeMethods.mouse_event(NativeMethods.MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        NativeMethods.mouse_event(NativeMethods.MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        return new AgentActionResult(true, $"clicked ({x},{y})", false);
    }

    private static AgentActionResult TypeText(AgentActionRequest request, CancellationToken cancellationToken)
    {
        if (!request.Arguments.TryGetValue("text", out var text) || string.IsNullOrEmpty(text))
        {
            return new AgentActionResult(false, "type_text requires text", false);
        }

        if (text.Length > 12000) return new AgentActionResult(false, "Text exceeds the 12,000 character limit. Split this into smaller steps.", false);

        // Unicode typing via KEYEVENTF_UNICODE (batched down+up per char for focus reliability).
        var inputSize = Marshal.SizeOf<NativeMethods.Input>();
        foreach (var ch in WindowsInputText(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pair = new NativeMethods.Input[2];
            pair[0] = new NativeMethods.Input
            {
                Type = 1,
                U = new NativeMethods.InputUnion
                {
                    Ki = new NativeMethods.KeybdInput
                    {
                        WVk = 0,
                        WScan = ch,
                        DwFlags = NativeMethods.KeyeventfUnicode,
                    },
                },
            };
            pair[1] = new NativeMethods.Input
            {
                Type = 1,
                U = new NativeMethods.InputUnion
                {
                    Ki = new NativeMethods.KeybdInput
                    {
                        WVk = 0,
                        WScan = ch,
                        DwFlags = NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup,
                    },
                },
            };
            var sent = NativeMethods.SendInput(2, pair, inputSize);
            if (sent != 2)
            {
                return new AgentActionResult(
                    false,
                    $"SendInput typed only {sent}/2 events for char U+{(int)ch:X4} (win32={Marshal.GetLastWin32Error()})",
                    false);
            }
        }

        return new AgentActionResult(true, $"typed {text.Length} chars", false);
    }

    internal static IEnumerable<char> WindowsInputText(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            // Windows edit controls expand CR to CRLF; sending both characters creates a blank line.
            if (text[index] != '\n' || index == 0 || text[index - 1] != '\r') yield return text[index];
        }
    }

    private static AgentActionResult Scroll(AgentActionRequest request)
    {
        var amount = 3;
        if (request.Arguments.TryGetValue("clicks", out var raw) && int.TryParse(raw, out var parsed))
        {
            amount = Math.Clamp(parsed, -20, 20);
        }

        // Positive clicks = scroll up (away from user) in Win32 wheel units.
        var wheel = amount * 120;
        NativeMethods.mouse_event(NativeMethods.MouseeventfWheel, 0, 0, unchecked((uint)wheel), UIntPtr.Zero);
        return new AgentActionResult(true, $"scrolled clicks={amount}", false);
    }

    private static AgentActionResult Hotkey(AgentActionRequest request)
    {
        if (!request.Arguments.TryGetValue("keys", out var keys) || string.IsNullOrWhiteSpace(keys))
        {
            return new AgentActionResult(false, "hotkey requires keys like ctrl+n", false);
        }

        var parts = keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<byte>();
        byte? main = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers.Add(0x11);
                    break;
                case "alt":
                    modifiers.Add(0x12);
                    break;
                case "shift":
                    modifiers.Add(0x10);
                    break;
                case "win":
                case "meta":
                    modifiers.Add(0x5B);
                    break;
                default:
                    if (main is not null) return new AgentActionResult(false, "A hotkey must contain exactly one main key.", false);
                    main = VirtualKeyFromName(part);
                    if (main is null) return new AgentActionResult(false, "Unsupported key in hotkey.", false);
                    break;
            }
        }

        if (main is null)
        {
            return new AgentActionResult(false, $"could not parse hotkey '{keys}'", false);
        }

        foreach (var mod in modifiers)
        {
            KeyEvent(mod, keyUp: false);
        }

        KeyEvent(main.Value, keyUp: false);
        KeyEvent(main.Value, keyUp: true);
        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            KeyEvent(modifiers[i], keyUp: true);
        }

        return new AgentActionResult(true, $"hotkey {keys}", false);
    }

    private static void KeyEvent(byte vk, bool keyUp)
    {
        var flags = keyUp ? NativeMethods.KeyeventfKeyup : 0u;
        NativeMethods.keybd_event(vk, 0, flags, UIntPtr.Zero);
    }

    private static byte? VirtualKeyFromName(string name)
    {
        if (name.Length == 1)
        {
            var ch = char.ToUpperInvariant(name[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return (byte)ch;
            }
        }

        return name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "f4" => 0x73,
            "f5" => 0x74,
            _ => null,
        };
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
        public const uint MouseeventfWheel = 0x0800;
        public const uint KeyeventfKeyup = 0x0002;
        public const uint KeyeventfUnicode = 0x0004;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        // INPUT must be 28 bytes (x86) / 40 bytes (x64). The union has to be sized to
        // MOUSEINPUT or SendInput returns 0 with ERROR_INVALID_PARAMETER (87).
        [StructLayout(LayoutKind.Sequential)]
        public struct Input
        {
            public int Type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mi;
            [FieldOffset(0)] public KeybdInput Ki;
            [FieldOffset(0)] public HardwareInput Hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MouseInput
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint DwFlags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KeybdInput
        {
            public ushort WVk;
            public ushort WScan;
            public uint DwFlags;
            public uint Time;
            public IntPtr DwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HardwareInput
        {
            public uint UMsg;
            public ushort WParamL;
            public ushort WParamH;
        }
    }
}

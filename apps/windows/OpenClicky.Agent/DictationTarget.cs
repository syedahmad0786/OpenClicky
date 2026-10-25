using System.Windows.Automation;
using OpenClicky.Core;

namespace OpenClicky.Agent;

public sealed class DictationTarget
{
    private readonly int[] _runtimeId;
    private readonly AutomationElement _element;

    private DictationTarget(IntPtr window, IntPtr focus, AutomationElement element, int[] runtimeId,
        string appTitle, string controlName, bool readable)
    {
        Window = window;
        Focus = focus;
        _element = element;
        _runtimeId = runtimeId;
        AppTitle = appTitle;
        ControlName = controlName;
        CanVerifyText = readable;
    }

    public IntPtr Window { get; }
    public IntPtr Focus { get; }
    public string AppTitle { get; }
    public string ControlName { get; }
    public bool CanVerifyText { get; }
    internal AutomationElement Element => _element;

    public static bool TryCapture(out DictationTarget? target, out string error, IEnumerable<string>? exclusions = null)
    {
        target = null;
        error = "Select an editable text field in another app, then start dictation again.";
        var window = ForegroundFocusRestorer.Capture();
        var title = ForegroundWindowInfo.GetTitle();
        if (AgentDenyList.IsDenied(title, exclusions))
        {
            error = "This app is excluded from dictation. Nothing was inspected.";
            return false;
        }
        var focus = ForegroundFocusRestorer.CaptureFocus(window);
        if (window == IntPtr.Zero || focus == IntPtr.Zero) return false;

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || element.Current.ProcessId == Environment.ProcessId) return false;
            if (!element.Current.IsEnabled || !element.Current.IsKeyboardFocusable)
            {
                error = "The selected control is disabled or cannot accept keyboard input.";
                return false;
            }
            if (element.Current.IsPassword)
            {
                error = "Dictation is unavailable in password or protected fields.";
                return false;
            }

            var value = TryValuePattern(element);
            if (value is { Current.IsReadOnly: true })
            {
                error = "The selected text field is read-only.";
                return false;
            }
            if (value is null)
            {
                error = "The selected control does not expose a writable text value. Choose a standard editable text field.";
                return false;
            }

            var name = string.IsNullOrWhiteSpace(element.Current.Name) ? element.Current.ControlType.LocalizedControlType : element.Current.Name;
            target = new(window, focus, element, element.GetRuntimeId(), title ?? "Selected app", name, readable: true);
            error = "";
            return true;
        }
        catch (ElementNotAvailableException)
        {
            error = "The selected field disappeared. Select it again and restart dictation.";
            return false;
        }
        catch (InvalidOperationException)
        {
            error = "The selected control cannot be used for dictation.";
            return false;
        }
    }

    public async Task<DictationInsertResult> InsertAsync(string text, IAgentActionExecutor executor,
        CancellationToken cancellationToken = default, bool requireCurrentFocus = false,
        IEnumerable<string>? exclusions = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(false, false, "Write or dictate some text before inserting.", false, null);
        if (text.Length > 12000) return new(false, false, "Dictation is limited to 12,000 characters.", false, null);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Revalidate(requireCurrentFocus, exclusions))
            return new(false, false, "The original app or text field changed. Nothing was inserted; select it again.", false, null);

        var beforeText = ReadText();
        var request = new AgentActionRequest(AgentToolNames.TypeText,
            new Dictionary<string, string> { ["text"] = text }, requireCurrentFocus ? "Insert finalized local dictation into the selected field" : "Insert the reviewed dictation draft");
        AgentActionResult action;
        try { action = await executor.ExecuteAsync(request, userConfirmed: true, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            return new(true, false, "Input was attempted but did not finish. The destination may contain partial text: " + ex.Message, false, null);
        }
        if (!action.Success)
            return new(true, false, "Input was attempted but did not finish. The destination may contain partial text: " + action.Message, false, null);

        await Task.Delay(80, cancellationToken).ConfigureAwait(false);
        var finalText = ReadText();
        if (finalText is null)
            return new(true, true, $"Inserted {text.Length} characters. The destination does not expose readable text, so insertion is unverified.", false, null);
        var verified = DictationInsertionProof.IsExactContiguousChange(beforeText, finalText, text);
        return verified
            ? new(true, true, $"Inserted and verified {text.Length} characters in the destination.", true, finalText)
            : new(true, true, "Input was sent, but the readable destination did not show the reviewed draft as a new change. Treat insertion as unverified.", false, finalText);
    }

    private bool Revalidate(bool requireCurrentFocus, IEnumerable<string>? exclusions)
    {
        if (requireCurrentFocus)
        {
            if (ForegroundFocusRestorer.Capture() != Window) return false;
        }
        else if (!ForegroundFocusRestorer.Restore(Window, focusedControl: Focus)) return false;
        if (AgentDenyList.IsDenied(ForegroundWindowInfo.GetTitle(), exclusions)) return false;
        try
        {
            var current = AutomationElement.FocusedElement;
            var value = current is null ? null : TryValuePattern(current);
            return current is not null && current.GetRuntimeId().SequenceEqual(_runtimeId)
                && current.Current.IsEnabled && current.Current.IsKeyboardFocusable && !current.Current.IsPassword
                && value is not null && !value.Current.IsReadOnly;
        }
        catch (ElementNotAvailableException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private string? ReadText()
    {
        try
        {
            return TryValuePattern(_element)?.Current.Value;
        }
        catch (ElementNotAvailableException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static ValuePattern? TryValuePattern(AutomationElement element) =>
        element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ? pattern as ValuePattern : null;

}

public sealed record DictationInsertResult(bool InputAttempted, bool InputCompleted, string Message, bool Verified, string? FinalText);

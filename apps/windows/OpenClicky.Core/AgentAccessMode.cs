using System.Text.RegularExpressions;
using OpenClicky.Core.Agent;

namespace OpenClicky.Core;

public enum AgentAccessMode { Preview, ConfirmEach, Auto, FullAccess }

public static class AgentAccessPolicy
{
    private static readonly Regex Sensitive = new(@"\b(send|submit|publish|launch|delete|remove|erase|pay|buy|purchase|checkout|transfer|invite|grant|authorize|password|credential|permission|security|disconnect|unsubscribe)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool RequiresConfirmation(AgentAccessMode mode, ActionRequest request)
    {
        if (mode == AgentAccessMode.FullAccess) return false;
        if (mode != AgentAccessMode.Auto) return true;
        // shortcut: rationale matching cannot prove a click is safe, use ConfirmEach for strict control.
        return request.ToolName == "hotkey" || string.IsNullOrWhiteSpace(request.Rationale)
            || Sensitive.IsMatch(request.Rationale + " " + string.Join(" ", request.Arguments.Values));
    }
}

public sealed class AgentTaskControl
{
    private readonly object _gate = new();
    private TaskCompletionSource? _resume;
    private long _pauseRevision;
    public bool IsPaused { get { lock (_gate) return _resume is not null; } }
    public long PauseRevision { get { lock (_gate) return _pauseRevision; } }
    public void Pause() { lock (_gate) { _pauseRevision++; _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously); } }
    public bool TryResume(long pauseRevision)
    {
        lock (_gate)
        {
            if (_resume is null || _pauseRevision != pauseRevision) return false;
            _resume.TrySetResult(); _resume = null; return true;
        }
    }
    public void Resume() { lock (_gate) { _resume?.TrySetResult(); _resume = null; } }
    public Task WaitAsync(CancellationToken token) { lock (_gate) return _resume?.Task.WaitAsync(token) ?? Task.CompletedTask; }
}

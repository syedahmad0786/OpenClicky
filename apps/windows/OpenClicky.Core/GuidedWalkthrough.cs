using System.Text.RegularExpressions;
using OpenClicky.Core.Drawing;

namespace OpenClicky.Core;

public enum WalkthroughMove { Start, Next, Back, Refresh }

public sealed record WalkthroughRequest(long Revision, string Prompt);

public sealed record WalkthroughView(
    bool Active,
    bool Busy,
    int Step,
    bool CanBack,
    string Goal,
    string Instruction,
    string Status);

public sealed partial class GuidedWalkthrough
{
    public const string ResponseInstructions = "\n" + """
        Guide the user through the stated goal using the fresh screenshots attached to each request.
        This is advisory guidance only: never perform actions or turn the request into an agent task.
        Return exactly one status marker: [GUIDE:step], [GUIDE:wait], or [GUIDE:complete].
        After the marker, give concise spoken text. You may also use existing [POINT:...] and [MARK:...] tags.
        For [GUIDE:step], briefly state the visible observation, then ask for one visible action only.
        Prefer one circle plus one point for that action; do not mark unrelated controls.
        On Next, first explicitly observe the visible result of the last action.
        Return [GUIDE:wait] when the expected result is missing or uncertain; do not advance.
        Return [GUIDE:complete] only when visible evidence proves the goal is complete.
        Back means freshly ground the earlier instruction in the current screenshots; it does not undo an action.
        Never reuse coordinates from an earlier response.
        """;

    private const int MaximumGoalLength = 2000;
    private const int MaximumInstructionLength = 1200;
    private const int MaximumStatusLength = 300;
    private const int MaximumHistory = 8;
    private readonly object _gate = new();
    private readonly List<(int Step, string Instruction)> _history = [];
    private WalkthroughView _view = new(false, false, 0, false, "", "", "");
    private WalkthroughRequest? _pending;
    private WalkthroughMove _pendingMove;
    private int _pendingStep;
    private long _revision;

    public WalkthroughView View { get { lock (_gate) return _view; } }

    public WalkthroughRequest Begin(string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        lock (_gate)
        {
            _history.Clear();
            var cleanGoal = goal.Trim();
            if (cleanGoal.Length > MaximumGoalLength)
                throw new ArgumentException("Keep the walkthrough goal under 2,000 characters.", nameof(goal));
            _view = new(true, true, 0, false, cleanGoal, "", "Looking at the screen…");
            return CreateRequest(WalkthroughMove.Start, 1);
        }
    }

    public WalkthroughRequest? Prepare(WalkthroughMove move)
    {
        lock (_gate)
        {
            if (!_view.Active || _view.Busy || move == WalkthroughMove.Start || move == WalkthroughMove.Back && !_view.CanBack)
                return null;

            var step = move == WalkthroughMove.Back ? _view.Step - 1 : _view.Step;
            _view = _view with { Busy = true, Status = "Looking at the screen…" };
            return CreateRequest(move, step);
        }
    }

    public string? Accept(WalkthroughRequest request, string raw)
    {
        ArgumentNullException.ThrowIfNull(request);
        raw ??= "";
        lock (_gate)
        {
            if (_pending is null || request != _pending || request.Revision != _revision) return null;

            var markers = GuideTags().Matches(raw);
            if (markers.Count != 1 || !TryKind(markers[0].Groups[1].Value, out var kind)
                || GuideLikeTags().Matches(raw).Count != 1)
            {
                RejectResponse();
                return null;
            }

            var clean = GuideTags().Replace(raw, "").Trim();
            var plain = Limit(VisualTags().Replace(DrawCommandParser.Parse(clean).Text, "").Trim(), MaximumInstructionLength);
            if (plain.Length == 0)
            {
                RejectResponse();
                return null;
            }

            _pending = null;
            if (kind == "wait")
            {
                var reviewedStep = _pendingMove == WalkthroughMove.Back ? _pendingStep : _view.Step;
                var status = _pendingMove == WalkthroughMove.Back
                    ? "Earlier step reviewed · Next checks the current result."
                    : "Waiting for the visible result.";
                _view = _view with { Busy = false, Step = reviewedStep, CanBack = HasStep(reviewedStep - 1), Instruction = plain, Status = status };
                return clean;
            }

            if (kind == "complete")
            {
                _view = _view with { Active = false, Busy = false, Instruction = plain, Status = "Complete" };
                return clean;
            }

            var step = _pendingMove == WalkthroughMove.Next ? _view.Step + 1 : Math.Max(1, _pendingStep);
            if (_pendingMove is WalkthroughMove.Start or WalkthroughMove.Next || _view.Step == 0)
            {
                _history.RemoveAll(entry => entry.Step >= step);
                _history.Add((step, plain));
                if (_history.Count > MaximumHistory) _history.RemoveAt(0);
            }
            _view = _view with { Busy = false, Step = step, CanBack = HasStep(step - 1), Instruction = plain, Status = $"Step {step}" };
            return clean;
        }
    }

    public void Fail(WalkthroughRequest request, string message)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_pending is null || request != _pending || request.Revision != _revision) return;
            _pending = null;
            var status = string.IsNullOrWhiteSpace(message) ? "Could not load guidance. Try again." : Limit(message.Trim(), MaximumStatusLength);
            _view = _view with { Busy = false, Status = status };
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _revision++;
            _pending = null;
            _view = _view with { Active = false, Busy = false, Status = "Stopped" };
        }
    }

    private WalkthroughRequest CreateRequest(WalkthroughMove move, int step)
    {
        var current = _history.FindLast(entry => entry.Step == _view.Step);
        var action = current == default ? "(none yet)" : current.Instruction;
        var prior = _history.Count == 0 ? "(none yet)" : string.Join("\n", _history.Select(entry => $"Step {entry.Step}: {entry.Instruction}"));
        var direction = move switch
        {
            WalkthroughMove.Start => "Give the first instruction.",
            WalkthroughMove.Next => "Observe the visible result of the last action, then give the next instruction or wait.",
            WalkthroughMove.Back => $"Freshly ground step {step} in the current screenshot. Do not undo anything.",
            _ => "Refresh the current instruction from the current screenshot without advancing."
        };
        _pendingMove = move;
        _pendingStep = step;
        _pending = new(++_revision, $"Goal: {_view.Goal}\nMove: {move}\nCurrent step: {_view.Step}\nLast action: {Limit(action, MaximumInstructionLength)}\nPrior plain instructions:\n{prior}\nRequest: {direction}");
        return _pending;
    }

    private void RejectResponse()
    {
        _pending = null;
        _view = _view with { Busy = false, Status = "The guide returned an invalid response. Try again." };
    }

    private static bool TryKind(string value, out string kind)
    {
        kind = value;
        return value is "step" or "wait" or "complete";
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private bool HasStep(int step) => step > 0 && _history.Any(entry => entry.Step == step);

    [GeneratedRegex(@"\[GUIDE:(step|wait|complete)\]", RegexOptions.CultureInvariant)]
    private static partial Regex GuideTags();

    [GeneratedRegex(@"\[GUIDE:[^\]\r\n]*(?:\]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex GuideLikeTags();

    // POINT/MARK close at their first ']'. DRAW is stripped by DrawCommandParser first (quote- and escape-aware close).
    [GeneratedRegex(@"\[(?:POINT|MARK):[^\]\r\n]*(?:\]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VisualTags();
}

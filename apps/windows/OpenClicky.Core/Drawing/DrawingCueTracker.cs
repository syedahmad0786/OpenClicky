namespace OpenClicky.Core.Drawing;

/// <summary>
/// Tracks the DRAW tags in a streaming reply and hands each closed one out once, cued to the spoken sentence it follows:
/// an op's sentence index is the number of sentence ends in the cleaned speech before its tag. Unclosed tags are never emitted.
/// Update and the Take calls may come from different threads (stream callback and speech queue).
/// </summary>
public sealed class DrawingCueTracker
{
    private readonly object _gate = new();
    private readonly List<(DrawOp Op, int Sentence)> _ops = [];
    private int _taken;

    /// <summary>Feeds the whole raw reply so far. Tags already seen keep their cue; new closed tags are added in order.</summary>
    public void Update(string rawPartial)
    {
        ArgumentNullException.ThrowIfNull(rawPartial);
        var ops = DrawCommandParser.Parse(rawPartial, partial: true).Ops;
        lock (_gate)
            for (var i = _ops.Count; i < ops.Count; i++)
                _ops.Add((ops[i], StreamingReplySpeech.CountSentenceEnds(StreamingReplySpeech.Clean(rawPartial[..ops[i].Offset]))));
    }

    /// <summary>The untaken ops, in order, cued at or before the given 0-based sentence.</summary>
    public IReadOnlyList<DrawOp> TakeThrough(int sentenceIndex)
    {
        lock (_gate)
        {
            var start = _taken;
            while (_taken < _ops.Count && _ops[_taken].Sentence <= sentenceIndex) _taken++;
            return _ops.GetRange(start, _taken - start).Select(o => o.Op).ToArray();
        }
    }

    public IReadOnlyList<DrawOp> TakeAll() => TakeThrough(int.MaxValue);
}

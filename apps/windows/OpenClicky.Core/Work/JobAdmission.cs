namespace OpenClicky.Core.Work;

/// <summary>
/// Which jobs may run now: up to <paramref name="max"/> at once, the rest wait in order. A job is never running or queued twice.
/// <see cref="Finish"/> moves the next queued job straight into the freed slot, so nothing can jump the queue in between.
/// Thread-safe: jobs start on the UI thread and finish on their own threads.
/// </summary>
public sealed class JobAdmission(int max = 5)
{
    public const string WaitingLine = "Waiting for a free slot";

    private readonly object _gate = new();
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly List<string> _queued = [];

    public int Running { get { lock (_gate) return _running.Count; } }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<string> Queued { get { lock (_gate) return _queued.ToArray(); } }

    /// <summary>Running or queued.</summary>
    public bool Contains(string jobId) { lock (_gate) return _running.Contains(jobId) || _queued.Contains(jobId); }

    /// <summary>Takes a slot; false when all are taken or the job is already running or queued.</summary>
    public bool TryStart(string jobId)
    {
        lock (_gate)
        {
            if (_running.Count >= max || _running.Contains(jobId) || _queued.Contains(jobId)) return false;
            _running.Add(jobId);
            return true;
        }
    }

    /// <summary>Queues the job at the back; a job already running or queued is left as it is.</summary>
    public void Enqueue(string jobId)
    {
        lock (_gate)
        {
            if (!_running.Contains(jobId) && !_queued.Contains(jobId)) _queued.Add(jobId);
        }
    }

    /// <summary>A running job ended: returns the queued job that now holds its slot (to be started), if any.
    /// A queued job leaves the queue (it was stopped before it started) and nothing else starts.</summary>
    public string? Finish(string jobId)
    {
        lock (_gate)
        {
            if (_queued.Remove(jobId) || !_running.Remove(jobId) || _queued.Count == 0) return null;
            var next = _queued[0];
            _queued.RemoveAt(0);
            _running.Add(next);
            return next;
        }
    }
}

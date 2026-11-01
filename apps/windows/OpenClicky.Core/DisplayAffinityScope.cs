namespace OpenClicky.Core;

/// <summary>
/// Sets WDA_EXCLUDEFROMCAPTURE on windows for the lifetime of the scope and puts every window back to the exact affinity
/// it had before. The Win32 calls are injected so the save/restore rule is testable.
/// </summary>
public sealed class DisplayAffinityScope : IDisposable
{
    public const uint None = 0x0;
    public const uint ExcludeFromCapture = 0x11;

    private readonly Func<nint, uint, bool> _set;
    private readonly List<(nint Handle, uint Previous)> _changed = [];
    private bool _disposed;

    /// <param name="get">Current affinity of a window, or null when it cannot be read.</param>
    /// <param name="set">SetWindowDisplayAffinity; false when Windows refused.</param>
    public DisplayAffinityScope(IEnumerable<nint> handles, Func<nint, uint?> get, Func<nint, uint, bool> set)
    {
        _set = set;
        foreach (var handle in handles.Where(h => h != 0).Distinct())
        {
            if (get(handle) is not { } previous) { Failed.Add(handle); continue; }
            if (previous == ExcludeFromCapture) { Excluded.Add(handle); continue; } // already hidden from capture: nothing to change or restore
            if (set(handle, ExcludeFromCapture)) { _changed.Add((handle, previous)); Excluded.Add(handle); }
            else Failed.Add(handle);
        }
    }

    /// <summary>Windows that are excluded from capture right now (already, or by this scope).</summary>
    public HashSet<nint> Excluded { get; } = [];

    /// <summary>Windows whose affinity could not be read or set; they are still visible to capture.</summary>
    public HashSet<nint> Failed { get; } = [];

    /// <summary>Windows this scope changed (and will restore).</summary>
    public int ChangedCount => _changed.Count;

    /// <summary>Windows whose previous affinity could not be put back.</summary>
    public int RestoreFailures { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _changed.Count - 1; i >= 0; i--)
        {
            var (handle, previous) = _changed[i];
            try { if (!_set(handle, previous)) RestoreFailures++; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { RestoreFailures++; }
        }
    }
}

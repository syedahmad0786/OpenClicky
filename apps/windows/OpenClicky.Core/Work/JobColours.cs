namespace OpenClicky.Core.Work;

/// <summary>The four job tag colours, stored by name. Order is the tie-break order.</summary>
public static class JobColours
{
    public static readonly IReadOnlyList<string> All = ["red", "blue", "yellow", "green"];

    /// <summary>The colour used least among <paramref name="inUse"/> (unknown names ignored); ties go to the earliest in <see cref="All"/>.</summary>
    public static string LeastUsed(IEnumerable<string> inUse)
    {
        var counts = new int[All.Count];
        foreach (var colour in inUse)
            if (IndexOf(colour) is var i and >= 0) counts[i]++;
        return All[Array.IndexOf(counts, counts.Min())];
    }

    /// <summary>The colour jobs had before colours were stored (FNV-1a of the id; string.GetHashCode is randomised per process).</summary>
    public static string FromId(string id)
    {
        var hash = 2166136261u;
        foreach (var c in id) hash = unchecked((hash ^ c) * 16777619u);
        return All[(int)(hash % (uint)All.Count)];
    }

    public static int IndexOf(string colour)
    {
        for (var i = 0; i < All.Count; i++) if (All[i] == colour) return i;
        return -1;
    }
}

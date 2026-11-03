namespace Simulator.RequestedQualification;

/// <summary>One immutable root identity and observed diagnostic keys; an empty list is not proof of support.</summary>
public sealed record BlockerObservation(string Id, string ContentHash, string[] Keys);
/// <summary>Number of distinct root inputs sharing a second observation.</summary>
public sealed record BlockerOverlap(string Key, int DistinctInputs);
/// <summary>Observed reach, with mirrored content and overlapping issues kept explicit.</summary>
public sealed record BlockerRank(string Key, int DistinctInputs, int UniqueContents, int SoleObservedInputs,
    string[] Examples, BlockerOverlap[] Cooccurring);
/// <summary>Offline evidence aggregation, independent of parser admission and numerical qualification.</summary>
public static class CorpusBlockerRanking
{
    /// <summary>Ranks root identities, never instance occurrences or guaranteed future successful solves.</summary>
    public static BlockerRank[] Rank(IEnumerable<BlockerObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var rows = observations.ToArray();
        // One row per frozen root identity. Counting a repeated report twice
        // would inflate both impact and overlap, so reject it explicitly.
        if (rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new ArgumentException("Duplicate root identity.", nameof(observations));
        var normalized = rows.Select(r => (r.Id, r.ContentHash,
            Keys: r.Keys.Distinct(StringComparer.Ordinal).ToArray())).ToArray();
        return normalized.SelectMany(r => r.Keys).Distinct(StringComparer.Ordinal).Select(key =>
        {
            var affected = normalized.Where(r => r.Keys.Contains(key, StringComparer.Ordinal)).ToArray();
            var overlap = affected.SelectMany(r => r.Keys.Where(k => k != key))
                .GroupBy(k => k, StringComparer.Ordinal)
                .Select(g => new BlockerOverlap(g.Key, g.Count()))
                .OrderByDescending(g => g.DistinctInputs).ThenBy(g => g.Key, StringComparer.Ordinal).Take(5).ToArray();
            return new BlockerRank(key, affected.Length,
                affected.Select(r => r.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                affected.Count(r => r.Keys.Length == 1),
                affected.Select(r => r.Id).Order(StringComparer.Ordinal).Take(3).ToArray(), overlap);
        }).OrderByDescending(r => r.DistinctInputs).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
    }
}

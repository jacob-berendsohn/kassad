using Kassad.Eval.Datasets;

namespace Kassad.Eval.Running;

/// <summary>
/// Picks a deterministic subset that keeps the full set's label proportions, for when rate limits make the full set
/// impractical (roadmap prerequisites: stratified samples of at least 500 rows, stated in the report). Rows are grouped
/// by label; each group gets its proportional share of the size (largest remainder, so the shares add up), is shuffled
/// with the seed and contributes that many rows. The result keeps the dataset's order, and the same rows, size and seed
/// always give the same sample: a seeded <see cref="Random"/> is stable across .NET versions.
/// </summary>
internal static class StratifiedSampler
{
    /// <summary>How the sample was drawn, recorded in the results file.</summary>
    public const string Method = "stratified by label: proportional shares (largest remainder), seeded shuffle within each label, dataset order kept";

    public static IReadOnlyList<EvalRow> Sample(IReadOnlyList<EvalRow> rows, int size, int seed)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        if (size >= rows.Count)
        {
            return rows;
        }

        var groups = rows
            .Select((row, position) => (Row: row, Position: position))
            .GroupBy(x => x.Row.Label)
            .OrderBy(g => g.Key)
            .Select(g => g.ToArray())
            .ToArray();

        var quotas = groups.Select(g => (double)g.Length * size / rows.Count).ToArray();
        var counts = quotas.Select(q => (int)Math.Floor(q)).ToArray();
        var leftover = size - counts.Sum();
        foreach (var i in Enumerable.Range(0, groups.Length).OrderByDescending(i => quotas[i] - counts[i]).ThenBy(i => i).Take(leftover))
        {
            counts[i]++;
        }

        var random = new Random(seed);
        var picked = new List<(EvalRow Row, int Position)>(size);
        for (var i = 0; i < groups.Length; i++)
        {
            random.Shuffle(groups[i]);
            picked.AddRange(groups[i].Take(counts[i]));
        }

        return picked.OrderBy(x => x.Position).Select(x => x.Row).ToArray();
    }
}

namespace Kassad.Eval.Datasets;

/// <summary>The datasets <c>--dataset</c> accepts, by name. One line per adapter.</summary>
internal static class EvalDatasets
{
    private static readonly IReadOnlyDictionary<string, IEvalDataset> ByName = new IEvalDataset[]
    {
        new DeepsetPromptInjections(),
    }.ToDictionary(d => d.Name, StringComparer.Ordinal);

    /// <summary>Every known dataset name, sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = ByName.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>The adapter for <paramref name="name"/>.</summary>
    /// <exception cref="EvalUsageException">No adapter has that name.</exception>
    public static IEvalDataset Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ByName.TryGetValue(name, out var dataset)
            ? dataset
            : throw new EvalUsageException($"Unknown dataset '{name}'. Known datasets: {string.Join(", ", Names)}.");
    }
}

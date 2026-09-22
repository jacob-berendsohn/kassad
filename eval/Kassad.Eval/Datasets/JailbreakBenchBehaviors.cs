using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// <c>JailbreakBench/JBB-Behaviors</c> on Hugging Face (MIT), config <c>behaviors</c>: 100 harmful behaviors, the goals an
/// attacker wants a model to carry out worded as direct requests (drawn from AdvBench, HarmBench/TDC and the
/// benchmark's own writing, ten per category), and 100 benign behaviors paired with them by topic and phrasing. The
/// text is the <c>Goal</c> column, the request as a user would type it, and the split is the label: 1 for
/// <c>harmful</c>, 0 for <c>benign</c>. These are plain harmful requests, not jailbreak prompts: the benchmark's
/// adversarial prompts (its artifacts) live in a separate repository and are not part of this adapter. The canonical
/// files are CSV, so <c>eval/datasets/jailbreakbench-behaviors.sh</c> fetches the rows as datasets-server pages, as for
/// deepset, and <see cref="RowsApiPages"/> reads them back.
/// </summary>
internal sealed class JailbreakBenchBehaviors : IEvalDataset
{
    /// <summary>Directory under the data directory that the download script fills.</summary>
    public const string DirectoryName = "jailbreakbench-behaviors";

    private const string HarmfulSplit = "harmful";

    private static readonly string[] SplitNames = [HarmfulSplit, "benign"];

    private static readonly string[] Columns = ["Goal"];

    public string Name => "jailbreakbench";

    public string Source => "https://huggingface.co/datasets/JailbreakBench/JBB-Behaviors";

    public string DownloadScript => "eval/datasets/jailbreakbench-behaviors.sh";

    public Stage Stage => Stage.Inbound;

    public string PositiveLabel => "harmful";

    public string StateShape => "{ user_message }";

    public object BuildState(EvalRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new UserMessageState(row.Text);
    }

    public Task<LoadedDataset> LoadAsync(string dataDir, CancellationToken cancellationToken) =>
        RowsApiPages.LoadAsync(this, dataDir, DirectoryName, SplitNames, Columns, Map, cancellationToken);

    private static EvalRow Map(string split, JsonElement row, int index)
    {
        var goal = row.GetProperty("Goal").GetString();
        if (string.IsNullOrWhiteSpace(goal))
        {
            throw new MalformedRowException("has an empty Goal");
        }

        return new EvalRow(split, index, goal, split == HarmfulSplit ? 1 : 0);
    }
}

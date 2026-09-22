using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// <c>lmsys/toxic-chat</c> on Hugging Face (CC BY-NC 4.0), config <c>toxicchat0124</c>, the version its maintainers
/// recommend: 5,082 train and 5,083 test user prompts collected from the Vicuna demo, each with a <c>toxicity</c> label
/// (about 7% are 1) and a separate <c>jailbreaking</c> flag (about 2%). The text is <c>user_input</c> and the label is
/// <c>toxicity</c>: a jailbreak attempt that is not toxic counts as 0 here, and the reply in <c>model_output</c> is not
/// used. Labels come from the maintainers' human-AI annotation pipeline (<c>human_annotation</c> says which rows a
/// person labeled); this adapter keeps every row. The canonical files are CSV, so <c>eval/datasets/toxicchat.sh</c>
/// fetches the rows as datasets-server pages, as for deepset, and <see cref="RowsApiPages"/> reads them back. The
/// license is non-commercial: the harness reads the prompts to measure the guardrail, and the committed results carry
/// hashes, labels and verdicts, never the text (Docs/research/eval-datasets.md).
/// </summary>
internal sealed class ToxicChat : IEvalDataset
{
    /// <summary>Directory under the data directory that the download script fills.</summary>
    public const string DirectoryName = "toxicchat-0124";

    private static readonly string[] SplitNames = ["train", "test"];

    private static readonly string[] Columns = ["user_input", "toxicity"];

    public string Name => "toxicchat";

    public string Source => "https://huggingface.co/datasets/lmsys/toxic-chat/viewer/toxicchat0124";

    public string DownloadScript => "eval/datasets/toxicchat.sh";

    public Stage Stage => Stage.Inbound;

    public string PositiveLabel => "toxic";

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
        var text = row.GetProperty("user_input").GetString() ?? throw new MalformedRowException("has a null user_input");
        var toxicity = row.GetProperty("toxicity").GetInt32();
        if (toxicity is not (0 or 1))
        {
            throw new MalformedRowException($"has toxicity {toxicity}; expected 0 or 1 (toxic)");
        }

        return new EvalRow(split, index, text, toxicity);
    }
}

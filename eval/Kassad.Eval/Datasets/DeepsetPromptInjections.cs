using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// <c>deepset/prompt-injections</c> on Hugging Face (Apache-2.0): 546 train and 116 test prompts, in English and
/// German, each labeled <c>1</c> for a prompt injection and <c>0</c> for a legitimate request. The canonical files
/// are Parquet, which .NET cannot read without a package, so <c>eval/datasets/deepset-prompt-injections.sh</c>
/// fetches both splits as JSON pages from the datasets-server rows API (100 rows each) into
/// <c>eval/data/deepset-prompt-injections/</c> together with a <c>manifest.json</c> (revision, download time) and
/// the repository's <c>dataset-info.json</c> (license); <see cref="RowsApiPages"/> reads those files back and refuses
/// anything truncated, duplicated or short of the row count the source reports.
/// </summary>
internal sealed class DeepsetPromptInjections : IEvalDataset
{
    /// <summary>Directory under the data directory that the download script fills.</summary>
    public const string DirectoryName = "deepset-prompt-injections";

    private static readonly string[] SplitNames = ["train", "test"];

    private static readonly string[] Columns = ["text", "label"];

    public string Name => "deepset";

    public string Source => "https://huggingface.co/datasets/deepset/prompt-injections";

    public string DownloadScript => "eval/datasets/deepset-prompt-injections.sh";

    public Stage Stage => Stage.Inbound;

    public string PositiveLabel => "injection";

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
        var text = row.GetProperty("text").GetString() ?? throw new MalformedRowException("has a null text");
        var label = row.GetProperty("label").GetInt32();
        if (label is not (0 or 1))
        {
            throw new MalformedRowException($"has label {label}; expected 0 (legitimate) or 1 (injection)");
        }

        return new EvalRow(split, index, text, label);
    }
}

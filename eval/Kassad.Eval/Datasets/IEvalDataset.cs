namespace Kassad.Eval.Datasets;

/// <summary>
/// A labeled public dataset the harness can evaluate. A download script under <c>eval/datasets/</c> puts the raw data
/// under the data directory (git-ignored, never committed); the adapter reads it back as rows of <c>(text, label)</c>
/// and says which stage's policies apply and what the state sent to the model looks like. Adding a dataset (roadmap
/// 4.3) means one script, one class and one line in <see cref="EvalDatasets"/>.
/// </summary>
internal interface IEvalDataset
{
    /// <summary>The name <c>--dataset</c> takes.</summary>
    string Name { get; }

    /// <summary>Where the data comes from, recorded in the results file.</summary>
    string Source { get; }

    /// <summary>The script that downloads it, relative to the repository root, named in the error when the data is missing.</summary>
    string DownloadScript { get; }

    /// <summary>The stage whose policies are evaluated; policies for other stages in the file are ignored.</summary>
    Stage Stage { get; }

    /// <summary>What a label of 1 means, in one word (e.g. <c>injection</c>): the class the guardrail should catch.</summary>
    string PositiveLabel { get; }

    /// <summary>How a row's text is presented to the model, for the results file (e.g. <c>{ user_message }</c>).</summary>
    string StateShape { get; }

    /// <summary>Read the downloaded rows and their provenance. Throws <see cref="EvalUsageException"/> when the data is missing or malformed.</summary>
    Task<LoadedDataset> LoadAsync(string dataDir, CancellationToken cancellationToken);

    /// <summary>The state the engine evaluates for one row.</summary>
    object BuildState(EvalRow row);
}

/// <summary>One labeled input.</summary>
/// <param name="Split">The source's split the row belongs to, e.g. <c>train</c>.</param>
/// <param name="Index">The row's index within its split, as the source numbers it.</param>
/// <param name="Text">The text to evaluate. Hashed, never copied, into the results file.</param>
/// <param name="Label">1 for the positive class the guardrail should catch, 0 otherwise.</param>
internal sealed record EvalRow(string Split, int Index, string Text, int Label)
{
    /// <summary><c>split/index</c>: joins a result row back to its source row without the text.</summary>
    public string Id => $"{Split}/{Index}";
}

/// <summary>The rows of a dataset in source order, and where they came from.</summary>
internal sealed record LoadedDataset(IReadOnlyList<EvalRow> Rows, DatasetProvenance Provenance);

/// <summary>What the download script recorded about the copy on disk. Any field is <c>null</c> when the script did not record it.</summary>
/// <param name="Revision">The source's revision (for Hugging Face, the repository commit) at download time.</param>
/// <param name="License">The license the source declares.</param>
/// <param name="DownloadedAt">When the script ran, UTC.</param>
/// <param name="Splits">The splits that were downloaded, in the order rows are listed.</param>
internal sealed record DatasetProvenance(string? Revision, string? License, DateTime? DownloadedAt, IReadOnlyList<string> Splits);

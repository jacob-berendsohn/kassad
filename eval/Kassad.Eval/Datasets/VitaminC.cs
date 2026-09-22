using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// <c>tals/vitaminc</c> on Hugging Face (CC BY-SA 3.0): the VitaminC fact-verification set (Schuster, Fisch and
/// Barzilay, NAACL 2021), claims paired with the one Wikipedia sentence that supports, refutes or does not address
/// them, built from real and synthetic Wikipedia revisions so that a claim recurs against contrastive evidence that
/// differs in a detail. The <c>test</c> split (55,197 rows) is the canonical <c>test.jsonl</c>, which .NET reads as it
/// is, fetched at a pinned revision by <c>eval/datasets/vitaminc.sh</c>; a run over a stratified sample of it is the
/// roadmap's "subset of a claim-verification set" for the grounding stage (Docs/research/eval-datasets.md). Each line
/// is one grounding pair: <c>claim</c> is the text, <c>evidence</c> the source passage, and the label is 1
/// (<c>unsupported</c>) for REFUTES and NOT ENOUGH INFO, 0 for SUPPORTS, the question <c>claim_unsupported</c> asks. No
/// <c>source_id</c> is sent (the page title would name the entity the claim is about), so the model judges the claim
/// against the passage alone. Text goes through verbatim, tokenization spaces and the source's mojibake included, so the
/// hashes match the file; a row's <c>index</c> is its position among the file's non-blank lines, from 0.
/// </summary>
internal sealed class VitaminC : IEvalDataset
{
    /// <summary>Directory under the data directory that the download script fills.</summary>
    public const string DirectoryName = "vitaminc";

    /// <summary>The canonical file the script fetches.</summary>
    public const string FileName = "test.jsonl";

    private const string SplitName = "test";

    private static readonly string[] SplitNames = [SplitName];

    public string Name => "vitaminc";

    public string Source => "https://huggingface.co/datasets/tals/vitaminc";

    public string DownloadScript => "eval/datasets/vitaminc.sh";

    public Stage Stage => Stage.Grounding;

    public string PositiveLabel => "unsupported";

    public string StateShape => "{ claim, source_passage }";

    public object BuildState(EvalRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new GroundingState(row.Text, row.SourcePassage ?? throw new ArgumentException("A VitaminC row carries its evidence as the source passage.", nameof(row)));
    }

    public async Task<LoadedDataset> LoadAsync(string dataDir, CancellationToken cancellationToken)
    {
        var directory = DownloadRecords.RequireDirectory(dataDir, DirectoryName, Name, DownloadScript);
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            throw new EvalUsageException($"Dataset '{Name}': {path} is missing. Run {DownloadScript} again.");
        }

        var rows = new List<EvalRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rows.Add(Map(path, lineNumber, line, rows.Count, seen));
        }

        if (rows.Count == 0)
        {
            throw Malformed(path, "it has no rows");
        }

        var manifest = await DownloadRecords.ReadManifestAsync(directory, DownloadScript, cancellationToken).ConfigureAwait(false);
        if (manifest.Rows is { } expected && rows.Count != expected)
        {
            throw new EvalUsageException($"Dataset '{Name}': {path} holds {rows.Count} rows but the manifest says {expected}. Run {DownloadScript} again.");
        }

        var license = await DownloadRecords.ReadLicenseAsync(directory, DownloadScript, cancellationToken).ConfigureAwait(false);
        return new LoadedDataset(rows, new DatasetProvenance(manifest.Revision, license, manifest.DownloadedAt, SplitNames));
    }

    private EvalRow Map(string path, int lineNumber, string line, int index, HashSet<string> seen)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var id = root.GetProperty("unique_id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Malformed(path, $"line {lineNumber} has no unique_id");
            }

            if (!seen.Add(id))
            {
                throw Malformed(path, $"line {lineNumber} repeats unique_id {id}");
            }

            var claim = root.GetProperty("claim").GetString();
            if (string.IsNullOrWhiteSpace(claim))
            {
                throw Malformed(path, $"line {lineNumber} has an empty claim");
            }

            var evidence = root.GetProperty("evidence").GetString();
            if (string.IsNullOrWhiteSpace(evidence))
            {
                throw Malformed(path, $"line {lineNumber} has an empty evidence");
            }

            var label = root.GetProperty("label").GetString() switch
            {
                "SUPPORTS" => 0,
                "REFUTES" or "NOT ENOUGH INFO" => 1,
                var other => throw Malformed(path, $"line {lineNumber} has label '{other}'; expected SUPPORTS, REFUTES or NOT ENOUGH INFO"),
            };

            return new EvalRow(SplitName, index, claim, label, evidence);
        }
        catch (JsonException ex)
        {
            throw Malformed(path, $"line {lineNumber} is not valid JSON ({ex.Message})");
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw Malformed(path, $"line {lineNumber}: {ex.Message}");
        }
    }

    private EvalUsageException Malformed(string path, string detail) => DownloadRecords.Malformed(path, detail, DownloadScript);
}

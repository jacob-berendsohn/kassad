using System.Globalization;
using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// <c>deepset/prompt-injections</c> on Hugging Face (Apache-2.0): 546 train and 116 test prompts, in English and
/// German, each labeled <c>1</c> for a prompt injection and <c>0</c> for a legitimate request. The canonical files
/// are Parquet, which .NET cannot read without a package, so <c>eval/datasets/deepset-prompt-injections.sh</c>
/// fetches both splits as JSON pages from the datasets-server rows API (100 rows each) into
/// <c>eval/data/deepset-prompt-injections/</c> together with a <c>manifest.json</c> (revision, download time) and
/// the repository's <c>dataset-info.json</c> (license); this adapter reads those files back and refuses anything
/// truncated, duplicated or short of the row count the source reports.
/// </summary>
internal sealed class DeepsetPromptInjections : IEvalDataset
{
    /// <summary>Directory under the data directory that the download script fills.</summary>
    public const string DirectoryName = "deepset-prompt-injections";

    private static readonly string[] SplitNames = ["train", "test"];

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

    public async Task<LoadedDataset> LoadAsync(string dataDir, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);

        var directory = Path.Combine(dataDir, DirectoryName);
        if (!Directory.Exists(directory))
        {
            throw new EvalUsageException($"Dataset '{Name}' is not downloaded: {directory} does not exist. Run {DownloadScript} first.");
        }

        var rows = new List<EvalRow>();
        foreach (var split in SplitNames)
        {
            rows.AddRange(await ReadSplitAsync(directory, split, cancellationToken).ConfigureAwait(false));
        }

        var provenance = await ReadProvenanceAsync(directory, cancellationToken).ConfigureAwait(false);
        return new LoadedDataset(rows, provenance);
    }

    private async Task<List<EvalRow>> ReadSplitAsync(string directory, string split, CancellationToken cancellationToken)
    {
        // Pages are named <split>-<offset, zero-padded>.json, so ordinal order is offset order.
        var pages = Directory.GetFiles(directory, $"{split}-*.json").Order(StringComparer.Ordinal).ToArray();
        if (pages.Length == 0)
        {
            throw new EvalUsageException($"Dataset '{Name}': no '{split}' pages in {directory}. Run {DownloadScript} again.");
        }

        var rows = new Dictionary<int, EvalRow>();
        int? reportedTotal = null;

        foreach (var page in pages)
        {
            using var document = await ParseAsync(page, cancellationToken).ConfigureAwait(false);
            try
            {
                var root = document.RootElement;
                if (root.TryGetProperty("num_rows_total", out var totalElement) && totalElement.TryGetInt32(out var total))
                {
                    reportedTotal ??= total;
                }

                if (!root.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
                {
                    throw Malformed(page, "it has no 'rows' array");
                }

                foreach (var entry in rowsElement.EnumerateArray())
                {
                    var index = entry.GetProperty("row_idx").GetInt32();
                    if (entry.TryGetProperty("truncated_cells", out var truncated) && truncated.ValueKind == JsonValueKind.Array && truncated.GetArrayLength() > 0)
                    {
                        throw Malformed(page, $"row {index} has truncated cells ({truncated}); the rows API cut it short");
                    }

                    var row = entry.GetProperty("row");
                    var text = row.GetProperty("text").GetString() ?? throw Malformed(page, $"row {index} has a null text");
                    var label = row.GetProperty("label").GetInt32();
                    if (label is not (0 or 1))
                    {
                        throw Malformed(page, $"row {index} has label {label}; expected 0 (legitimate) or 1 (injection)");
                    }

                    if (!rows.TryAdd(index, new EvalRow(split, index, text, label)))
                    {
                        throw Malformed(page, $"row {index} appears twice");
                    }
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw Malformed(page, ex.Message);
            }
        }

        if (reportedTotal is { } expected && rows.Count != expected)
        {
            throw new EvalUsageException($"Dataset '{Name}': the '{split}' pages hold {rows.Count} rows but the source reports {expected}. Run {DownloadScript} again.");
        }

        return rows.Values.OrderBy(r => r.Index).ToList();
    }

    private static async Task<DatasetProvenance> ReadProvenanceAsync(string directory, CancellationToken cancellationToken)
    {
        string? revision = null;
        DateTime? downloadedAt = null;
        string? license = null;

        var manifestPath = Path.Combine(directory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            using var manifest = await ParseAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            revision = ReadString(manifest.RootElement, "revision");
            var downloaded = ReadString(manifest.RootElement, "downloaded_at");
            if (downloaded is not null && DateTimeOffset.TryParse(downloaded, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                downloadedAt = at.UtcDateTime;
            }
        }

        var infoPath = Path.Combine(directory, "dataset-info.json");
        if (File.Exists(infoPath))
        {
            using var info = await ParseAsync(infoPath, cancellationToken).ConfigureAwait(false);
            if (info.RootElement.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object)
            {
                license = ReadString(card, "license");
            }
        }

        return new DatasetProvenance(revision, license, downloadedAt, SplitNames);
    }

    private static async Task<JsonDocument> ParseAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw Malformed(path, $"it is not valid JSON ({ex.Message})");
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static EvalUsageException Malformed(string path, string detail) =>
        new($"Dataset file {path} is malformed: {detail}. Re-run eval/datasets/deepset-prompt-injections.sh.");
}

using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// Reads the JSON pages a download script fetched from the Hugging Face datasets-server rows API
/// (<c>https://datasets-server.huggingface.co/rows</c>) for a dataset whose canonical files .NET cannot read without a
/// package: files named <c>&lt;split&gt;-&lt;offset&gt;.json</c>, each holding <c>rows[].row_idx</c>,
/// <c>rows[].row.&lt;column&gt;</c>, <c>rows[].truncated_cells</c> and <c>num_rows_total</c>. Every adapter over that API
/// (deepset, JailbreakBench, ToxicChat) maps its own columns to an <see cref="EvalRow"/> and shares the checks here: a
/// page that is not JSON or has no rows, a row whose needed column the API cut short, a row index that appears twice
/// and a split shorter than the source reports are refused with the download script named.
/// </summary>
internal static class RowsApiPages
{
    /// <summary>
    /// Load every split of a rows-API dataset: the directory must exist (else the "not downloaded" error names the script),
    /// each split's pages are read in order, and the provenance comes from <see cref="DownloadRecords"/>.
    /// </summary>
    /// <param name="dataset">The adapter, for its name and download script.</param>
    /// <param name="dataDir">The data directory (<c>--data-dir</c>).</param>
    /// <param name="directoryName">The dataset's directory under it, as the script names it.</param>
    /// <param name="splits">The splits the script downloaded, in row order.</param>
    /// <param name="columns">The columns <paramref name="map"/> reads; a row whose truncated cells include one is refused.</param>
    /// <param name="map">Maps a split name, a page row (<c>rows[].row</c>) and its index to an <see cref="EvalRow"/>; throws <see cref="MalformedRowException"/> for a value it cannot accept.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<LoadedDataset> LoadAsync(
        IEvalDataset dataset,
        string dataDir,
        string directoryName,
        IReadOnlyList<string> splits,
        IReadOnlyCollection<string> columns,
        Func<string, JsonElement, int, EvalRow> map,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(splits);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(map);

        var directory = DownloadRecords.RequireDirectory(dataDir, directoryName, dataset.Name, dataset.DownloadScript);

        var rows = new List<EvalRow>();
        foreach (var split in splits)
        {
            rows.AddRange(await ReadSplitAsync(directory, split, dataset.Name, dataset.DownloadScript, columns, (row, index) => map(split, row, index), cancellationToken).ConfigureAwait(false));
        }

        var provenance = await DownloadRecords.ReadProvenanceAsync(directory, splits, dataset.DownloadScript, cancellationToken).ConfigureAwait(false);
        return new LoadedDataset(rows, provenance);
    }

    /// <summary>Read one split's pages into rows ordered by index.</summary>
    /// <param name="directory">The dataset's directory.</param>
    /// <param name="split">The split; its pages are <c>{split}-*.json</c>, named so that ordinal order is offset order.</param>
    /// <param name="datasetName">The adapter's name, for messages.</param>
    /// <param name="downloadScript">Named in every message.</param>
    /// <param name="columns">The columns <paramref name="map"/> reads; a row whose truncated cells include one is refused.</param>
    /// <param name="map">Maps a page row (<c>rows[].row</c>) and its index to an <see cref="EvalRow"/>; throws <see cref="MalformedRowException"/> for a value it cannot accept.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="EvalUsageException">No pages, a malformed page or row, a duplicated index, or fewer rows than the source reports.</exception>
    public static async Task<List<EvalRow>> ReadSplitAsync(
        string directory,
        string split,
        string datasetName,
        string downloadScript,
        IReadOnlyCollection<string> columns,
        Func<JsonElement, int, EvalRow> map,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(split);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(map);

        var pages = Directory.GetFiles(directory, $"{split}-*.json").Order(StringComparer.Ordinal).ToArray();
        if (pages.Length == 0)
        {
            throw new EvalUsageException($"Dataset '{datasetName}': no '{split}' pages in {directory}. Run {downloadScript} again.");
        }

        var rows = new Dictionary<int, EvalRow>();
        int? reportedTotal = null;

        foreach (var page in pages)
        {
            using var document = await DownloadRecords.ParseAsync(page, downloadScript, cancellationToken).ConfigureAwait(false);
            try
            {
                var root = document.RootElement;
                if (root.TryGetProperty("num_rows_total", out var totalElement) && totalElement.TryGetInt32(out var total))
                {
                    reportedTotal ??= total;
                }

                if (!root.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
                {
                    throw DownloadRecords.Malformed(page, "it has no 'rows' array", downloadScript);
                }

                foreach (var entry in rowsElement.EnumerateArray())
                {
                    var index = entry.GetProperty("row_idx").GetInt32();
                    if (entry.TryGetProperty("truncated_cells", out var truncated) && truncated.ValueKind == JsonValueKind.Array)
                    {
                        var cut = truncated.EnumerateArray()
                            .Where(c => c.ValueKind == JsonValueKind.String)
                            .Select(c => c.GetString()!)
                            .Where(columns.Contains)
                            .ToArray();
                        if (cut.Length > 0)
                        {
                            throw DownloadRecords.Malformed(page, $"row {index} has truncated cells ({string.Join(", ", cut)}); the rows API cut it short", downloadScript);
                        }
                    }

                    EvalRow row;
                    try
                    {
                        row = map(entry.GetProperty("row"), index);
                    }
                    catch (MalformedRowException ex)
                    {
                        throw DownloadRecords.Malformed(page, $"row {index} {ex.Message}", downloadScript);
                    }

                    if (!rows.TryAdd(index, row))
                    {
                        throw DownloadRecords.Malformed(page, $"row {index} appears twice", downloadScript);
                    }
                }
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw DownloadRecords.Malformed(page, ex.Message, downloadScript);
            }
        }

        if (reportedTotal is { } expected && rows.Count != expected)
        {
            throw new EvalUsageException($"Dataset '{datasetName}': the '{split}' pages hold {rows.Count} rows but the source reports {expected}. Run {downloadScript} again.");
        }

        return rows.Values.OrderBy(r => r.Index).ToList();
    }
}

/// <summary>
/// Thrown by an adapter's row mapper for a value it cannot accept (a null text, a label outside 0 and 1);
/// <see cref="RowsApiPages"/> reports it as a malformed page, naming the row index and the download script.
/// </summary>
internal sealed class MalformedRowException : Exception
{
    public MalformedRowException(string message) : base(message)
    {
    }
}

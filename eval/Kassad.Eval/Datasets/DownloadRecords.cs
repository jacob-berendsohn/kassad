using System.Globalization;
using System.Text.Json;

namespace Kassad.Eval.Datasets;

/// <summary>
/// What every download script leaves beside the data: <c>manifest.json</c> (the source's revision at download time,
/// when the script ran, how many rows it saw) and <c>dataset-info.json</c> (the Hugging Face repository record, whose
/// <c>cardData.license</c> the results file records). Both are optional: a field the script did not record is
/// <c>null</c>. Also the directory check and the JSON parse every adapter starts with.
/// </summary>
internal static class DownloadRecords
{
    /// <summary>The scripts' record of what they fetched.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The Hugging Face repository record (<c>https://huggingface.co/api/datasets/&lt;id&gt;</c>) as the script saved it.</summary>
    public const string DatasetInfoFileName = "dataset-info.json";

    /// <summary>The dataset's directory under the data directory, which must exist.</summary>
    /// <exception cref="EvalUsageException">It does not; the message names the script.</exception>
    public static string RequireDirectory(string dataDir, string directoryName, string datasetName, string downloadScript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);

        var directory = Path.Combine(dataDir, directoryName);
        if (!Directory.Exists(directory))
        {
            throw new EvalUsageException($"Dataset '{datasetName}' is not downloaded: {directory} does not exist. Run {downloadScript} first.");
        }

        return directory;
    }

    /// <summary>The manifest's revision and download time and the repository record's license, for the results file.</summary>
    public static async Task<DatasetProvenance> ReadProvenanceAsync(string directory, IReadOnlyList<string> splits, string downloadScript, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(directory, downloadScript, cancellationToken).ConfigureAwait(false);
        var license = await ReadLicenseAsync(directory, downloadScript, cancellationToken).ConfigureAwait(false);
        return new DatasetProvenance(manifest.Revision, license, manifest.DownloadedAt, splits);
    }

    /// <summary>The manifest's <c>revision</c>, <c>downloaded_at</c> (UTC) and <c>rows</c>; every field <c>null</c> when there is no manifest or it lacks the field.</summary>
    public static async Task<Manifest> ReadManifestAsync(string directory, string downloadScript, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path))
        {
            return new Manifest(null, null, null);
        }

        using var manifest = await ParseAsync(path, downloadScript, cancellationToken).ConfigureAwait(false);
        var root = manifest.RootElement;

        DateTime? downloadedAt = null;
        if (ReadString(root, "downloaded_at") is { } downloaded && DateTimeOffset.TryParse(downloaded, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            downloadedAt = at.UtcDateTime;
        }

        int? rows = root.TryGetProperty("rows", out var rowsElement) && rowsElement.TryGetInt32(out var count) ? count : null;
        return new Manifest(ReadString(root, "revision"), downloadedAt, rows);
    }

    /// <summary>
    /// The repository record's <c>cardData.license</c>: one string, or several joined with commas (Hugging Face lets a card
    /// list more than one); <c>null</c> when there is no record or it declares none.
    /// </summary>
    public static async Task<string?> ReadLicenseAsync(string directory, string downloadScript, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, DatasetInfoFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        using var info = await ParseAsync(path, downloadScript, cancellationToken).ConfigureAwait(false);
        if (!info.RootElement.TryGetProperty("cardData", out var card) || card.ValueKind != JsonValueKind.Object || !card.TryGetProperty("license", out var license))
        {
            return null;
        }

        return license.ValueKind switch
        {
            JsonValueKind.String => Nonblank(license.GetString()),
            JsonValueKind.Array => Nonblank(string.Join(", ", license.EnumerateArray()
                .Where(l => l.ValueKind == JsonValueKind.String)
                .Select(l => l.GetString())
                .Where(l => !string.IsNullOrWhiteSpace(l)))),
            _ => null,
        };
    }

    /// <summary>Parse one downloaded file.</summary>
    /// <exception cref="EvalUsageException">It is not valid JSON; the message names the file and the script.</exception>
    public static async Task<JsonDocument> ParseAsync(string path, string downloadScript, CancellationToken cancellationToken)
    {
        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            throw Malformed(path, $"it is not valid JSON ({ex.Message})", downloadScript);
        }
    }

    /// <summary>A string property, or <c>null</c> when it is absent, not a string or blank.</summary>
    public static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Nonblank(value.GetString()) : null;

    /// <summary>The error for a downloaded file the adapter cannot accept.</summary>
    public static EvalUsageException Malformed(string path, string detail, string downloadScript) =>
        new($"Dataset file {path} is malformed: {detail}. Re-run {downloadScript}.");

    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>What <c>manifest.json</c> recorded; each field <c>null</c> when it did not.</summary>
/// <param name="Revision">The source's revision (for Hugging Face, the repository commit) at download time.</param>
/// <param name="DownloadedAt">When the script ran, UTC.</param>
/// <param name="Rows">How many rows the script saw, for datasets whose files do not state their own count.</param>
internal sealed record Manifest(string? Revision, DateTime? DownloadedAt, int? Rows);

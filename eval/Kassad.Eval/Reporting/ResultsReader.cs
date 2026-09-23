using System.Text.Json;
using Kassad.Eval.Results;

namespace Kassad.Eval.Reporting;

/// <summary>
/// Reads the results files <c>kassad-eval run</c> writes (schema version 1, <c>eval/README.md</c>) back for the report.
/// The envelope reuses the writer's records; rows and verdicts get reading records of their own that keep only what the
/// metrics need. Missing required fields, nulls where the schema has none and other schema versions are refused with
/// the file named, never guessed at.
/// </summary>
internal static class ResultsReader
{
    /// <summary>The schema version this reader understands.</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>
    /// Every <c>*.json</c> file directly in <paramref name="directory"/>, in ordinal file-name order (dated names sort by
    /// date), each read and checked.
    /// </summary>
    /// <exception cref="EvalUsageException">The directory is missing or holds no results file, or a file cannot be read as one.</exception>
    public static async Task<IReadOnlyList<ResultsDocument>> ReadDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new EvalUsageException($"{directory}: no such directory.");
        }

        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new EvalUsageException($"{directory}: no results files (*.json). Write one with kassad-eval run.");
        }

        var documents = new List<ResultsDocument>(files.Length);
        foreach (var file in files)
        {
            documents.Add(await ReadFileAsync(file, cancellationToken).ConfigureAwait(false));
        }

        return documents;
    }

    /// <summary>Read and check one results file.</summary>
    /// <exception cref="EvalUsageException">The file cannot be read as a schema-1 results file.</exception>
    public static async Task<ResultsDocument> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var name = Path.GetFileName(path);
        ResultsDocument? document;
        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                document = await JsonSerializer.DeserializeAsync<ResultsDocument>(stream, Options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            throw new EvalUsageException($"{path}: not a results file this report can read ({ex.Message})", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvalUsageException($"{path}: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new EvalUsageException($"{path}: empty results file.");
        }

        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw new EvalUsageException($"{path}: schema version {document.SchemaVersion}; this report reads version {SupportedSchemaVersion}.");
        }

        var policyIds = document.Policies.Evaluated.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var row in document.Rows)
        {
            if (row.Label is not (0 or 1))
            {
                throw new EvalUsageException($"{path}: row {row.Id} has label {row.Label}; labels are 0 or 1.");
            }

            foreach (var id in policyIds)
            {
                if (!row.Verdicts.ContainsKey(id))
                {
                    throw new EvalUsageException($"{path}: row {row.Id} has no verdict for policy {id}.");
                }
            }
        }

        return document with { FileName = name };
    }
}

/// <summary>A results file as the report reads it: the writer's envelope records plus reading records for the rows.</summary>
internal sealed record ResultsDocument(
    int SchemaVersion,
    RunInfo Run,
    DatasetInfo Dataset,
    PoliciesInfo Policies,
    ModelInfo Model,
    IReadOnlyList<ReportRow> Rows)
{
    /// <summary>The file's name, set by the reader; the report cites it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileName { get; init; } = string.Empty;
}

/// <summary>
/// The fields of a result row the metrics use, plus the hashes <c>kassad-eval compare</c> matches rows on (optional
/// here so that hand-written test documents without them still report; the comparison requires them).
/// </summary>
internal sealed record ReportRow(
    string Id,
    int Label,
    double LatencyMs,
    int? InputTokens,
    string? Error,
    IReadOnlyDictionary<string, ReportVerdict> Verdicts,
    string? TextSha256 = null,
    string? PassageSha256 = null);

/// <summary>The fields of a verdict the metrics use. Answer fields are absent on error verdicts and for other question types.</summary>
internal sealed record ReportVerdict(
    string Type,
    string Action,
    bool FromError,
    double? Probability = null,
    string? Choice = null,
    JsonElement? Probabilities = null,
    double? Score = null,
    double? Confidence = null);

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Kassad.Eval.Results;

/// <summary>
/// Writes a <see cref="RunResults"/> as one JSON document: the envelope indented for reading, then <c>rows</c> as one
/// compact object per line, so a diff of a committed file shows one line per changed row and a ten-thousand-row file
/// stays a few megabytes. snake_case names throughout, LF line endings, a trailing newline, no BOM. The relaxed encoder
/// keeps the + of a MinVer version, the arrows in error reasons and any non-ASCII readable: the file is data, never
/// embedded in HTML.
/// </summary>
internal static class ResultsWriter
{
    private static readonly JsonSerializerOptions EnvelopeOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions RowOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The whole document as text.</summary>
    public static string Render(RunResults results)
    {
        ArgumentNullException.ThrowIfNull(results);

        // The envelope ends with "\n}"; the rows array is spliced in ahead of that closing brace.
        var envelope = JsonSerializer.Serialize(results, EnvelopeOptions).TrimEnd();
        var builder = new StringBuilder(envelope.Length + (results.Rows.Count * 512));
        builder.Append(envelope, 0, envelope.Length - 1).Append(",\n  \"rows\": [\n");
        for (var i = 0; i < results.Rows.Count; i++)
        {
            builder.Append("    ").Append(JsonSerializer.Serialize(results.Rows[i], RowOptions));
            builder.Append(i < results.Rows.Count - 1 ? ",\n" : "\n");
        }

        builder.Append("  ]\n}\n");
        return builder.ToString();
    }

    /// <summary>Render and write, creating the directory when needed. An existing file is replaced.</summary>
    public static async Task WriteAsync(string path, RunResults results, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (Path.GetDirectoryName(fullPath) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, Render(results), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    }
}

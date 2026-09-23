using System.Text;

namespace Kassad.Eval.Reporting;

/// <summary>
/// Puts the rendered report into the README between two marker comments (roadmap 4.4), so the numbers there are
/// generated, never typed. Everything outside the markers is left byte for byte; the file's line endings and byte
/// order mark are kept; a second run with the same results files changes nothing, which CI checks with
/// <c>git diff --exit-code</c>.
/// </summary>
internal static class ReadmeUpdater
{
    /// <summary>Line after which the report is inserted.</summary>
    public const string StartMarker = "<!-- numbers:start -->";

    /// <summary>Line before which the report ends.</summary>
    public const string EndMarker = "<!-- numbers:end -->";

    /// <summary>Replace what stands between the markers of the file with <paramref name="report"/>. Returns true when the file changed.</summary>
    /// <exception cref="EvalUsageException">The file is missing or does not carry exactly one of each marker, each on a line of its own, start before end.</exception>
    public static async Task<bool> UpdateFileAsync(string path, string report, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvalUsageException($"{path}: {ex.Message}", ex);
        }

        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

        string updated;
        try
        {
            updated = Replace(text, report);
        }
        catch (FormatException ex)
        {
            throw new EvalUsageException($"{path}: {ex.Message}", ex);
        }

        if (updated == text)
        {
            return false;
        }

        await File.WriteAllTextAsync(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The README text with the lines between the markers replaced by <paramref name="report"/>, whose LF line endings
    /// are converted when the README uses CRLF. The marker lines themselves stay.
    /// </summary>
    /// <exception cref="FormatException">The markers are missing, repeated, out of order or not alone on their lines.</exception>
    public static string Replace(string readme, string report)
    {
        ArgumentNullException.ThrowIfNull(readme);
        ArgumentNullException.ThrowIfNull(report);

        var (startLineEnd, _) = Locate(readme, StartMarker);
        var (_, endLineStart) = Locate(readme, EndMarker);
        if (endLineStart < startLineEnd)
        {
            throw new FormatException($"'{EndMarker}' comes before '{StartMarker}'.");
        }

        var newline = readme.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var body = newline == "\n" ? report : report.Replace("\n", newline, StringComparison.Ordinal);
        return string.Concat(readme.AsSpan(0, startLineEnd), body, readme.AsSpan(endLineStart));
    }

    /// <summary>
    /// Find the one line that holds <paramref name="marker"/> and nothing else: returns the index just after that line's
    /// newline (or the text's end) and the index where the line starts.
    /// </summary>
    private static (int AfterLine, int LineStart) Locate(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new FormatException($"'{marker}' is missing; add the two marker lines where the numbers go.");
        }

        if (text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal) >= 0)
        {
            throw new FormatException($"'{marker}' appears more than once.");
        }

        var lineStart = text.LastIndexOf('\n', index) + 1;
        var lineBreak = text.IndexOf('\n', index + marker.Length);
        var lineEnd = lineBreak < 0 ? text.Length : lineBreak;
        var afterLine = lineBreak < 0 ? text.Length : lineBreak + 1;

        if (!text.AsSpan(lineStart, index - lineStart).IsWhiteSpace() || !text.AsSpan(index + marker.Length, lineEnd - index - marker.Length).IsWhiteSpace())
        {
            throw new FormatException($"'{marker}' must be on a line of its own.");
        }

        return (afterLine, lineStart);
    }
}

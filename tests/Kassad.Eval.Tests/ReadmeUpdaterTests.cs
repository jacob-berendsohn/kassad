using System.Text;
using Kassad.Eval.Reporting;

namespace Kassad.Eval.Tests;

/// <summary>The marker replacement behind <c>report --update-readme</c> (roadmap 4.4): what it keeps, what it converts, what it refuses.</summary>
public sealed class ReadmeUpdaterTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private const string Readme = "# Kassad\n\nProse before.\n\n## Numbers\n\nWhat the numbers mean.\n\n<!-- numbers:start -->\nstale line 1\nstale line 2\n<!-- numbers:end -->\n\nProse after.\n";

    [Fact]
    public void Replaces_the_lines_between_the_markers_and_keeps_everything_else()
    {
        var updated = ReadmeUpdater.Replace(Readme, "### Summary\n\n| a |\n|---|\n");

        Assert.Equal("# Kassad\n\nProse before.\n\n## Numbers\n\nWhat the numbers mean.\n\n<!-- numbers:start -->\n### Summary\n\n| a |\n|---|\n<!-- numbers:end -->\n\nProse after.\n", updated);
    }

    [Fact]
    public void Running_twice_changes_nothing()
    {
        var once = ReadmeUpdater.Replace(Readme, "report\n");

        Assert.Equal(once, ReadmeUpdater.Replace(once, "report\n"));
    }

    [Fact]
    public void An_empty_region_is_filled_and_an_empty_report_empties_it()
    {
        const string empty = "<!-- numbers:start -->\n<!-- numbers:end -->\n";

        Assert.Equal("<!-- numbers:start -->\nreport\n<!-- numbers:end -->\n", ReadmeUpdater.Replace(empty, "report\n"));
        Assert.Equal(empty, ReadmeUpdater.Replace("<!-- numbers:start -->\nold\n<!-- numbers:end -->\n", string.Empty));
    }

    [Fact]
    public void A_crlf_readme_gets_the_report_in_crlf()
    {
        var crlf = Readme.Replace("\n", "\r\n", StringComparison.Ordinal);

        var updated = ReadmeUpdater.Replace(crlf, "line 1\nline 2\n");

        Assert.Contains("<!-- numbers:start -->\r\nline 1\r\nline 2\r\n<!-- numbers:end -->\r\n", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", updated.Replace("\r\n", "|", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Markers_may_be_indented_but_must_stand_alone_on_their_lines()
    {
        Assert.Equal("  <!-- numbers:start -->\nreport\n  <!-- numbers:end -->\n", ReadmeUpdater.Replace("  <!-- numbers:start -->\nold\n  <!-- numbers:end -->\n", "report\n"));

        var ex = Assert.Throws<FormatException>(() => ReadmeUpdater.Replace("text <!-- numbers:start -->\nold\n<!-- numbers:end -->\n", "report\n"));
        Assert.Contains("must be on a line of its own", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no markers at all\n", "'<!-- numbers:start -->' is missing")]
    [InlineData("<!-- numbers:start -->\nold\n", "'<!-- numbers:end -->' is missing")]
    [InlineData("<!-- numbers:start -->\n<!-- numbers:start -->\n<!-- numbers:end -->\n", "'<!-- numbers:start -->' appears more than once")]
    [InlineData("<!-- numbers:end -->\nold\n<!-- numbers:start -->\n", "comes before")]
    public void Missing_repeated_or_reversed_markers_are_refused(string readme, string message)
    {
        var ex = Assert.Throws<FormatException>(() => ReadmeUpdater.Replace(readme, "report\n"));

        Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_file_keeps_its_byte_order_mark_and_reports_whether_it_changed()
    {
        var path = Path.Combine(_dir.Root, "README.md");
        await File.WriteAllTextAsync(path, Readme, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.True(await ReadmeUpdater.UpdateFileAsync(path, "report\n", CancellationToken.None));
        Assert.False(await ReadmeUpdater.UpdateFileAsync(path, "report\n", CancellationToken.None));

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("<!-- numbers:start -->\nreport\n<!-- numbers:end -->\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_without_markers_is_left_alone_and_named()
    {
        var path = Path.Combine(_dir.Root, "README.md");
        await File.WriteAllTextAsync(path, "nothing here\n");

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadmeUpdater.UpdateFileAsync(path, "report\n", CancellationToken.None));

        Assert.Contains("README.md", ex.Message, StringComparison.Ordinal);
        Assert.Contains("is missing", ex.Message, StringComparison.Ordinal);
        Assert.Equal("nothing here\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task A_missing_file_is_refused_by_name()
    {
        var path = Path.Combine(_dir.Root, "nope.md");

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadmeUpdater.UpdateFileAsync(path, "report\n", CancellationToken.None));

        Assert.Contains("nope.md", ex.Message, StringComparison.Ordinal);
    }
}

using System.Text.Json;
using Kassad.Eval.Datasets;

namespace Kassad.Eval.Tests;

public sealed class VitaminCTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly VitaminC _dataset = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Describes_itself_as_the_grounding_set()
    {
        Assert.Equal("vitaminc", _dataset.Name);
        Assert.Equal(Stage.Grounding, _dataset.Stage);
        Assert.Equal("unsupported", _dataset.PositiveLabel);
        Assert.Equal("{ claim, source_passage }", _dataset.StateShape);
        Assert.Equal("eval/datasets/vitaminc.sh", _dataset.DownloadScript);
        Assert.StartsWith("https://huggingface.co/datasets/tals/vitaminc", _dataset.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loads_lines_in_order_mapping_the_three_labels_and_carrying_the_evidence_as_the_passage()
    {
        _dir.WithVitaminC(VitaminCLines.Six);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Equal(Enumerable.Range(0, 6).Select(i => $"test/{i}"), loaded.Rows.Select(r => r.Id));
        Assert.Equal(VitaminCLines.Labels, loaded.Rows.Select(r => r.Label));
        Assert.All(loaded.Rows, r => Assert.Equal("test", r.Split));
        Assert.Equal(["test"], loaded.Provenance.Splits);

        using var first = JsonDocument.Parse(VitaminCLines.Six[0]);
        Assert.Equal(first.RootElement.GetProperty("claim").GetString(), loaded.Rows[0].Text);
        Assert.Equal(first.RootElement.GetProperty("evidence").GetString(), loaded.Rows[0].SourcePassage);
        Assert.All(loaded.Rows, r => Assert.NotNull(r.SourcePassage));
    }

    [Fact]
    public async Task Text_is_verbatim_including_the_sources_tokenization_spaces_and_mojibake()
    {
        _dir.WithVitaminC(VitaminCLines.Six);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.EndsWith(" .", loaded.Rows[0].Text, StringComparison.Ordinal);
        Assert.Contains("9.8ï¿½million", loaded.Rows[2].SourcePassage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_provenance_including_a_license_list()
    {
        _dir.WithVitaminC(VitaminCLines.Six);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Equal("be6febb761b0b2807687e61e0b5282e459df2fa0", loaded.Provenance.Revision);
        Assert.Equal("cc-by-sa-3.0", loaded.Provenance.License);
        Assert.Equal(new DateTime(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc), loaded.Provenance.DownloadedAt);
    }

    [Fact]
    public async Task Provenance_is_null_when_the_script_recorded_none()
    {
        _dir.WithVitaminC(VitaminCLines.Six, withProvenance: false);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Null(loaded.Provenance.Revision);
        Assert.Null(loaded.Provenance.License);
        Assert.Null(loaded.Provenance.DownloadedAt);
        Assert.Equal(6, loaded.Rows.Count);
    }

    [Fact]
    public async Task A_missing_download_names_the_script()
    {
        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("eval/datasets/vitaminc.sh", ex.Message, StringComparison.Ordinal);
        Assert.Contains(VitaminC.DirectoryName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_names_the_script()
    {
        Directory.CreateDirectory(_dir.VitaminCDir);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains(VitaminC.FileName, ex.Message, StringComparison.Ordinal);
        Assert.Contains("eval/datasets/vitaminc.sh", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fewer_rows_than_the_manifest_says_is_an_error()
    {
        _dir.WithVitaminC(VitaminCLines.Six, manifestRows: 10);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("holds 6 rows but the manifest says 10", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_label_is_an_error_naming_the_line()
    {
        var lines = VitaminCLines.Six.ToArray();
        lines[1] = lines[1].Replace("\"REFUTES\"", "\"MAYBE\"", StringComparison.Ordinal);
        _dir.WithVitaminC(lines);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        // The fixture puts a blank line after the first row, so the second row is line 3.
        Assert.Contains("line 3 has label 'MAYBE'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("eval/datasets/vitaminc.sh", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_that_is_not_json_is_an_error()
    {
        var lines = VitaminCLines.Six.ToArray();
        lines[2] = "{ not json";
        _dir.WithVitaminC(lines);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("line 4 is not valid JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeated_unique_id_is_an_error()
    {
        var lines = VitaminCLines.Six.ToArray();
        lines[3] = lines[3].Replace("\"75397_1\"", "\"5eafed7ec9e77c0009ce0b67_1\"", StringComparison.Ordinal);
        _dir.WithVitaminC(lines);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("repeats unique_id 5eafed7ec9e77c0009ce0b67_1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_claim_is_an_error()
    {
        var lines = VitaminCLines.Six.ToArray();
        lines[5] = lines[5].Replace("\"claim\": \"Roman Atwood is a content creator .\"", "\"claim\": \" \"", StringComparison.Ordinal);
        _dir.WithVitaminC(lines);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("line 7 has an empty claim", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_state_is_a_grounding_state_without_a_source_id()
    {
        var state = _dataset.BuildState(new EvalRow("test", 0, "The claim .", 1, "The passage ."));

        var grounding = Assert.IsType<GroundingState>(state);
        Assert.Equal("The claim .", grounding.Claim);
        Assert.Equal("The passage .", grounding.SourcePassage);
        Assert.Null(grounding.SourceId);
    }

    [Fact]
    public void A_row_without_a_passage_cannot_become_a_state()
    {
        Assert.Throws<ArgumentException>(() => _dataset.BuildState(new EvalRow("test", 0, "The claim .", 1)));
    }
}

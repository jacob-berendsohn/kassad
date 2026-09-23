using System.CommandLine;
using System.Text;
using Kassad.Tests;

namespace Kassad.Eval.Tests;

/// <summary>The two roadmap 4.4 entry points end to end: <c>report --update-readme</c> and <c>compare</c>.</summary>
public sealed class CliReadmeAndCompareTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        _dir.Dispose();
        _stdout.Dispose();
        _stderr.Dispose();
    }

    private static readonly string GoldenDir = Path.Combine(AppContext.BaseDirectory, "TestData", "report-golden");

    private const string Readme = "# Kassad\n\n## Numbers\n\nWhat they mean.\n\n<!-- numbers:start -->\n*Not yet measured.*\n<!-- numbers:end -->\n\n## Design notes\n";

    private static FakeDecisionModel Answering(double pYes = 0.05, string choice = "general", double pProhibited = 0.1, double confidence = 0.7) => new FakeDecisionModel()
        .Answer("prompt_injection", new NoulAnswer(pYes))
        .Answer("request_class", new ChoiceAnswer(
            choice,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["support"] = 0.1, ["general"] = 0.9 - pProhibited, ["prohibited"] = pProhibited },
            confidence));

    private Task<int> InvokeAsync(FakeDecisionModel model, params string[] args) =>
        Cli.Build(model, _stdout, _stderr).Parse(args).InvokeAsync(new InvocationConfiguration { Output = _stdout, Error = _stderr });

    private async Task<string> RunDeepsetAsync(FakeDecisionModel model, string fileName)
    {
        var policies = _dir.WritePolicies(TestPolicyFiles.InboundAndOutbound);
        var output = _dir.OutPath(fileName);
        Assert.Equal(ExitCodes.Ok, await InvokeAsync(model, "run", "--dataset", "deepset", "--policies", policies, "--out", output, "--data-dir", _dir.Root));
        _stdout.GetStringBuilder().Clear();
        _stderr.GetStringBuilder().Clear();
        return output;
    }

    [Fact]
    public async Task Update_readme_writes_the_report_between_the_markers_and_a_second_run_changes_nothing()
    {
        var readme = Path.Combine(_dir.Root, "README.md");
        await File.WriteAllTextAsync(readme, Readme, new UTF8Encoding(false));
        var expected = "# Kassad\n\n## Numbers\n\nWhat they mean.\n\n<!-- numbers:start -->\n" + await File.ReadAllTextAsync(Path.Combine(GoldenDir, "golden-20.expected.md")) + "<!-- numbers:end -->\n\n## Design notes\n";

        var exit = await InvokeAsync(Answering(), "report", "--in", GoldenDir, "--update-readme", readme);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(string.Empty, _stdout.ToString());
        Assert.Equal($"kassad-eval: {readme}: numbers section rewritten from 1 results file\n", _stderr.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(expected, await File.ReadAllTextAsync(readme));
        _stderr.GetStringBuilder().Clear();

        var before = await File.ReadAllBytesAsync(readme);
        exit = await InvokeAsync(Answering(), "report", "--in", GoldenDir, "--update-readme", readme);

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(before, await File.ReadAllBytesAsync(readme));
        Assert.Equal($"kassad-eval: {readme}: numbers section already up to date\n", _stderr.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_readme_keeps_crlf_line_endings()
    {
        var readme = Path.Combine(_dir.Root, "README.md");
        await File.WriteAllTextAsync(readme, Readme.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(ExitCodes.Ok, await InvokeAsync(Answering(), "report", "--in", GoldenDir, "--update-readme", readme));

        var text = await File.ReadAllTextAsync(readme);
        Assert.Contains("<!-- numbers:start -->\r\n### Summary\r\n\r\n| Dataset |", text, StringComparison.Ordinal);
        Assert.EndsWith("do not edit by hand.\r\n<!-- numbers:end -->\r\n\r\n## Design notes\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", text.Replace("\r\n", "|", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_readme_without_markers_exits_1_and_leaves_the_file()
    {
        var readme = Path.Combine(_dir.Root, "README.md");
        await File.WriteAllTextAsync(readme, "# Kassad\n\nNo markers.\n");

        var exit = await InvokeAsync(Answering(), "report", "--in", GoldenDir, "--update-readme", readme);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("'<!-- numbers:start -->' is missing", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("# Kassad\n\nNo markers.\n", await File.ReadAllTextAsync(readme));
    }

    [Fact]
    public async Task Update_readme_with_a_missing_file_exits_1()
    {
        var exit = await InvokeAsync(Answering(), "report", "--in", GoldenDir, "--update-readme", Path.Combine(_dir.Root, "nope.md"));

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("nope.md", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_requires_baseline_and_candidate_and_a_tolerance_in_range()
    {
        Assert.Equal(ExitCodes.Fatal, await InvokeAsync(Answering(), "compare"));
        Assert.Contains("--baseline", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("--candidate", _stderr.ToString(), StringComparison.Ordinal);
        _stderr.GetStringBuilder().Clear();

        Assert.Equal(ExitCodes.Fatal, await InvokeAsync(Answering(), "compare", "--baseline", "b.json", "--candidate", "c.json", "--tolerance", "1.5"));
        Assert.Contains("--tolerance must be between 0 and 1.", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_of_two_identical_runs_exits_0_and_prints_the_table()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var baseline = await RunDeepsetAsync(Answering(), "2026-09-23-deepset.json");
        var candidate = await RunDeepsetAsync(Answering(), "candidate.json");

        var exit = await InvokeAsync(Answering(), "compare", "--baseline", baseline, "--candidate", candidate);

        Assert.Equal(ExitCodes.Ok, exit);
        var markdown = _stdout.ToString();
        Assert.StartsWith("### Drift check: deepset\n\nBaseline `2026-09-23-deepset.json` (answered by `fake`) against candidate `candidate.json` (answered by `fake`): 5 candidate rows, 5 compared; 0 baseline and 0 candidate error rows left out. Same policy file. Tolerance 0.050, absolute.\n\n", markdown, StringComparison.Ordinal);
        Assert.Contains("| (stage) | stage outcome changed | n/a | 0.000 | 0.000 | yes |\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith("Within tolerance: no number moved more than 0.050.\n", markdown, StringComparison.Ordinal);
        Assert.Equal(string.Empty, _stderr.ToString());
    }

    [Fact]
    public async Task Compare_exits_4_when_the_verdicts_moved_more_than_the_tolerance()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var baseline = await RunDeepsetAsync(Answering(), "2026-09-23-deepset.json");
        var candidate = await RunDeepsetAsync(Answering(pYes: 0.99), "candidate.json");

        var exit = await InvokeAsync(Answering(), "compare", "--baseline", baseline, "--candidate", candidate, "--tolerance", "0.02");

        Assert.Equal(ExitCodes.Drifted, exit);
        var markdown = _stdout.ToString();
        Assert.Contains("| (stage) | stage outcome changed | n/a | 1.000 | 1.000 | no |\n", markdown, StringComparison.Ordinal);
        Assert.Contains("| `prompt_injection` | `block` recall | 0.000 | 1.000 | +1.000 | no |\n", markdown, StringComparison.Ordinal);
        Assert.Contains("\nDRIFT: ", markdown, StringComparison.Ordinal);
        Assert.Contains("moved more than 0.020", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_takes_the_newest_baseline_for_the_dataset_from_a_directory()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        await RunDeepsetAsync(Answering(pYes: 0.99), "2026-09-20-deepset.json");
        var newest = await RunDeepsetAsync(Answering(), "2026-09-21-deepset.json");
        var candidate = await RunDeepsetAsync(Answering(), "candidate.json");
        var otherDir = Path.Combine(_dir.Root, "candidate-dir");
        Directory.CreateDirectory(otherDir);
        File.Move(candidate, Path.Combine(otherDir, "deepset.json"));

        var exit = await InvokeAsync(Answering(), "compare", "--baseline", Path.GetDirectoryName(newest)!, "--candidate", Path.Combine(otherDir, "deepset.json"));

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Contains("Baseline `2026-09-21-deepset.json`", _stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_against_a_directory_without_a_run_of_the_dataset_exits_1()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var candidate = await RunDeepsetAsync(Answering(), "candidate.json");
        var empty = Path.Combine(_dir.Root, "other-results");
        Directory.CreateDirectory(empty);
        await File.WriteAllTextAsync(Path.Combine(empty, "2026-09-22-vitaminc.json"), await File.ReadAllTextAsync(Path.Combine(GoldenDir, "golden-20.json")));

        var exit = await InvokeAsync(Answering(), "compare", "--baseline", empty, "--candidate", candidate);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("no results file for dataset 'deepset'", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, _stdout.ToString());
    }

    [Fact]
    public async Task Compare_refuses_runs_that_are_not_over_the_same_rows()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var baseline = await RunDeepsetAsync(Answering(), "2026-09-23-deepset.json");
        var candidate = Path.Combine(_dir.Root, "edited.json");
        await File.WriteAllTextAsync(candidate, (await File.ReadAllTextAsync(baseline)).Replace("\"id\":\"train/1\"", "\"id\":\"train/9\"", StringComparison.Ordinal));

        var exit = await InvokeAsync(Answering(), "compare", "--baseline", baseline, "--candidate", candidate);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("row train/9 is not in 2026-09-23-deepset.json", _stderr.ToString(), StringComparison.Ordinal);
    }
}

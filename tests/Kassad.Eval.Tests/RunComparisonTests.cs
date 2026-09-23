using Kassad.Eval.Reporting;

namespace Kassad.Eval.Tests;

/// <summary>
/// <c>kassad-eval compare</c> (roadmap 4.4) over hand-written results documents: a noul and a choice policy, four
/// rows, so every expected number is a fraction of small integers.
/// </summary>
public sealed class RunComparisonTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private const string Policies = """
        [
          { "id": "prompt_injection", "type": "noul", "thresholds": { "flag": 0.4, "review": 0.6, "block": 0.85 }, "actions": null, "min_confidence": 0, "on_error": "fail_closed" },
          { "id": "request_class", "type": "choice", "thresholds": null, "actions": { "prohibited": { "action": "block", "min_confidence": 0.7 } }, "min_confidence": 0.3, "on_error": "fail_open" }
        ]
        """;

    /// <summary>A row: the noul's p(yes) and action, the choice's option, p(prohibited), confidence and action; an error row has no answers.</summary>
    private static string Row(int index, int label, double pYes, string noulAction, string choice, double pProhibited, double confidence, string choiceAction, string? error = null, string? sha = null, string split = "test")
    {
        sha ??= $"hash-{index}";
        var id = $"{split}/{index}";
        // Four dollars: the JSON ends in three consecutive braces, which a two-dollar raw literal would read as a hole.
        if (error is not null)
        {
            return FormattableString.Invariant($$$$"""{"id":"{{{{id}}}}","label":{{{{label}}}},"text_sha256":"{{{{sha}}}}","latency_ms":5000,"input_tokens":null,"error":"{{{{error}}}}","verdicts":{"prompt_injection":{"type":"noul","action":"block","from_error":true,"error":"{{{{error}}}}"},"request_class":{"type":"choice","action":"allow","from_error":true,"error":"{{{{error}}}}"}}}""");
        }

        return FormattableString.Invariant($$$$"""{"id":"{{{{id}}}}","label":{{{{label}}}},"text_sha256":"{{{{sha}}}}","latency_ms":{{{{100 + index}}}},"input_tokens":500,"error":null,"verdicts":{"prompt_injection":{"type":"noul","probability":{{{{pYes}}}},"value":{{{{pYes}}}},"action":"{{{{noulAction}}}}","from_error":false},"request_class":{"type":"choice","choice":"{{{{choice}}}}","probabilities":{"general":{{{{1 - pProhibited}}}},"prohibited":{{{{pProhibited}}}}},"confidence":{{{{confidence}}}},"value":{{{{(choice == "prohibited" ? pProhibited : 1 - pProhibited)}}}},"action":"{{{{choiceAction}}}}","from_error":false}}}""");
    }

    private static string Document(string rows, string dataset = "deepset", string stage = "inbound", string policies = Policies, string sha256 = "abc", string resolved = "\"jev-1.13.0\"") => $$"""
        {
          "schema_version": 1,
          "run": { "started_at": "2026-09-23T00:00:00Z", "finished_at": "2026-09-23T00:00:01Z", "duration_ms": 1000, "concurrency": 4, "interrupted": false },
          "dataset": { "name": "{{dataset}}", "source": "memory", "revision": null, "license": null, "downloaded_at": null, "splits": ["test"],
                       "rows_total": 4, "rows_evaluated": 4, "sample": null, "positive_label": "injection", "stage": "{{stage}}", "state_shape": "{ user_message }" },
          "policies": { "file": "p.json", "sha256": "{{sha256}}", "stage": "{{stage}}", "evaluated": {{policies}} },
          "model": { "name": "typesafe:jev-latest", "resolved": [{{resolved}}] },
          "summary": { "rows": 4, "errors": 0, "outcomes": {}, "input_tokens": 0, "output_tokens": 0 },
          "rows": [ {{rows}} ]
        }
        """;

    /// <summary>Two injections (blocked, blocked) and two legitimate rows (allowed, one reviewed for low confidence).</summary>
    private static readonly string[] BaselineRows =
    [
        Row(0, 1, 0.99, "block", "prohibited", 0.95, 0.95, "block"),
        Row(1, 1, 0.90, "block", "general", 0.10, 0.90, "allow"),
        Row(2, 0, 0.02, "allow", "general", 0.01, 0.99, "allow"),
        Row(3, 0, 0.05, "allow", "general", 0.20, 0.20, "review"),
    ];

    private async Task<ResultsDocument> ReadAsync(string json, string name)
    {
        var path = Path.Combine(_dir.Root, name);
        await File.WriteAllTextAsync(path, json);
        return await ResultsReader.ReadFileAsync(path, CancellationToken.None);
    }

    private async Task<ComparisonResult> CompareAsync(IEnumerable<string> candidateRows, double tolerance = RunComparison.DefaultTolerance, string candidateDocument = "")
    {
        var baseline = await ReadAsync(Document(string.Join(",", BaselineRows)), "2026-09-23-deepset.json");
        var candidate = await ReadAsync(candidateDocument.Length > 0 ? candidateDocument : Document(string.Join(",", candidateRows)), "candidate.json");
        return RunComparison.Compare(baseline, candidate, tolerance);
    }

    [Fact]
    public async Task A_run_compared_with_itself_moves_nothing()
    {
        var result = await CompareAsync(BaselineRows);

        Assert.True(result.WithinTolerance);
        Assert.Empty(result.Drifted);
        Assert.Equal(4, result.CandidateRows);
        Assert.Equal(4, result.MatchedRows);
        Assert.Equal((0, 0), (result.BaselineErrorRows, result.CandidateErrorRows));
        Assert.True(result.SamePolicyFile);
        Assert.All(result.Numbers, n => Assert.True(n.Delta is 0));

        // File-level share, then per policy: the changed share, ROC AUC and precision/recall per configured level (block for the noul; review and block for the choice).
        Assert.Equal(
            ["(stage) stage outcome changed", "prompt_injection action changed", "prompt_injection ROC AUC", "prompt_injection `flag` precision", "prompt_injection `flag` recall", "prompt_injection `review` precision", "prompt_injection `review` recall", "prompt_injection `block` precision", "prompt_injection `block` recall", "request_class action changed", "request_class ROC AUC", "request_class `review` precision", "request_class `review` recall", "request_class `block` precision", "request_class `block` recall"],
            result.Numbers.Select(n => $"{n.Policy} {n.Name}"));
        Assert.Equal(1.0, result.Numbers.Single(n => n.Name == "ROC AUC" && n.Policy == "prompt_injection").Baseline);
    }

    [Fact]
    public async Task A_flipped_verdict_shows_in_the_shares_and_in_recall()
    {
        // Row 1's injection now scores 0.30, below every threshold: allow instead of block, so the stage outcome and the noul's action change on one row of four.
        var candidate = BaselineRows.ToArray();
        candidate[1] = Row(1, 1, 0.30, "allow", "general", 0.10, 0.90, "allow");

        var result = await CompareAsync(candidate);

        Assert.False(result.WithinTolerance);
        var outcome = result.Numbers.Single(n => n.Policy == RunComparison.StageLabel);
        Assert.Equal((null, 0.25, 0.25), (outcome.Baseline, outcome.Candidate, outcome.Delta));
        Assert.Equal(0.25, result.Numbers.Single(n => n.Policy == "prompt_injection" && n.Name == "action changed").Delta);
        Assert.Equal(0.0, result.Numbers.Single(n => n.Policy == "request_class" && n.Name == "action changed").Delta);

        // Recall at every level 2/2 -> 1/2; precision stays 1.000 (the other injection is still blocked); AUC stays 1.000 (0.30 still ranks above both legitimate rows).
        var blockRecall = result.Numbers.Single(n => n.Policy == "prompt_injection" && n.Name == "`block` recall");
        Assert.Equal((1.0, 0.5, -0.5), (blockRecall.Baseline, blockRecall.Candidate, blockRecall.Delta));
        Assert.Equal(0.0, result.Numbers.Single(n => n.Policy == "prompt_injection" && n.Name == "`block` precision").Delta);
        Assert.Equal(1.0, result.Numbers.Single(n => n.Policy == "prompt_injection" && n.Name == "ROC AUC").Candidate);

        Assert.Equal(["(stage) stage outcome changed", "prompt_injection action changed", "prompt_injection `flag` recall", "prompt_injection `review` recall", "prompt_injection `block` recall"], result.Drifted.Select(n => $"{n.Policy} {n.Name}"));
        Assert.Contains("DRIFT: 5 numbers moved more than 0.050: (stage) stage outcome changed; prompt_injection action changed; prompt_injection `flag` recall; prompt_injection `review` recall; prompt_injection `block` recall.", result.ToMarkdown(), StringComparison.Ordinal);

        // A tolerance that admits half the rows changing passes the same comparison.
        Assert.True((await CompareAsync(candidate, tolerance: 0.5)).WithinTolerance);
    }

    [Fact]
    public async Task Error_rows_on_either_side_are_left_out_and_counted()
    {
        var baseline = await ReadAsync(Document(string.Join(",", BaselineRows[0], BaselineRows[1], Row(2, 0, 0, "allow", "general", 0, 0, "allow", error: "boom"), BaselineRows[3])), "b.json");
        var candidate = await ReadAsync(Document(string.Join(",", BaselineRows[0], Row(1, 1, 0, "allow", "general", 0, 0, "allow", error: "timeout"), BaselineRows[2], BaselineRows[3])), "c.json");

        var result = RunComparison.Compare(baseline, candidate, 0.05);

        Assert.Equal(4, result.CandidateRows);
        Assert.Equal(2, result.MatchedRows);
        Assert.Equal((1, 1), (result.BaselineErrorRows, result.CandidateErrorRows));
        Assert.True(result.WithinTolerance);
        Assert.Contains("4 candidate rows, 2 compared; 1 baseline and 1 candidate error rows left out.", result.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_candidate_that_is_a_sample_of_the_baseline_compares_its_rows_only()
    {
        var result = await CompareAsync([BaselineRows[0], BaselineRows[3]]);

        Assert.Equal((2, 2), (result.CandidateRows, result.MatchedRows));
        Assert.True(result.WithinTolerance);
    }

    [Fact]
    public async Task A_candidate_row_the_baseline_lacks_is_refused()
    {
        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => CompareAsync([.. BaselineRows, Row(4, 1, 0.99, "block", "prohibited", 0.9, 0.9, "block")]));

        Assert.Contains("row test/4 is not in 2026-09-23-deepset.json", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other-hash", 1)]
    [InlineData("hash-1", 0)]
    public async Task A_row_whose_text_or_label_differs_is_refused(string sha, int label)
    {
        var candidate = BaselineRows.ToArray();
        candidate[1] = Row(1, label, 0.90, "block", "general", 0.10, 0.90, "allow", sha: sha);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => CompareAsync(candidate));

        Assert.Contains("row test/1 differs from the same row", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_without_a_text_hash_cannot_be_matched()
    {
        var rows = string.Join(",", BaselineRows.Select(r => r.Replace("\"text_sha256\":\"hash-", "\"other\":\"hash-", StringComparison.Ordinal)));

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => CompareAsync([], candidateDocument: Document(rows)));

        Assert.Contains("no text_sha256", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_of_different_datasets_or_settings_are_refused()
    {
        var other = await Assert.ThrowsAsync<EvalUsageException>(() => CompareAsync([], candidateDocument: Document(string.Join(",", BaselineRows), dataset: "toxicchat")));
        Assert.Contains("is a deepset run and candidate.json a toxicchat run", other.Message, StringComparison.Ordinal);

        var retuned = Policies.Replace("\"block\": 0.85", "\"block\": 0.6", StringComparison.Ordinal);
        var settings = await Assert.ThrowsAsync<EvalUsageException>(() => CompareAsync([], candidateDocument: Document(string.Join(",", BaselineRows), policies: retuned)));
        Assert.Contains("different policy settings", settings.Message, StringComparison.Ordinal);
        Assert.Contains("block 0.85", settings.Message, StringComparison.Ordinal);
        Assert.Contains("block 0.6", settings.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_precision_defined_on_one_side_only_is_shown_but_not_gated()
    {
        // No baseline row reaches block for the choice; the candidate blocks one legitimate row: precision n/a -> 0.000, recall 0 -> 0.
        var baseline = await ReadAsync(Document(string.Join(",", Row(0, 1, 0.99, "block", "general", 0.1, 0.9, "allow"), BaselineRows[2])), "b.json");
        var candidate = await ReadAsync(Document(string.Join(",", Row(0, 1, 0.99, "block", "general", 0.1, 0.9, "allow"), Row(2, 0, 0.02, "allow", "prohibited", 0.9, 0.9, "block"))), "c.json");

        var result = RunComparison.Compare(baseline, candidate, 1.0);

        var precision = result.Numbers.Single(n => n.Policy == "request_class" && n.Name == "`block` precision");
        Assert.Equal((null, 0.0, null), (precision.Baseline, precision.Candidate, precision.Delta));
        Assert.True(precision.Within(0));
        Assert.Contains("| `request_class` | `block` precision | n/a | 0.000 | n/a | n/a |", result.ToMarkdown(), StringComparison.Ordinal);
        Assert.Equal(0.0, result.Numbers.Single(n => n.Policy == "request_class" && n.Name == "`block` recall").Delta);
    }

    [Fact]
    public async Task The_header_names_files_releases_and_the_policy_file()
    {
        var baseline = await ReadAsync(Document(string.Join(",", BaselineRows)), "2026-09-23-deepset.json");
        var candidate = await ReadAsync(Document(string.Join(",", BaselineRows), sha256: "def", resolved: "\"jev-1.13.0\", \"jev-1.14.0\""), "deepset.json");

        var markdown = RunComparison.Compare(baseline, candidate, 0.05).ToMarkdown();

        Assert.StartsWith("### Drift check: deepset\n\nBaseline `2026-09-23-deepset.json` (answered by `jev-1.13.0`) against candidate `deepset.json` (answered by `jev-1.13.0`, `jev-1.14.0`): 4 candidate rows, 4 compared; 0 baseline and 0 candidate error rows left out. Different policy file bytes, same policy settings. Tolerance 0.050, absolute.\n\n", markdown, StringComparison.Ordinal);
        Assert.Contains("| Policy | Number | Baseline | Candidate | Delta | Within tolerance |\n|---|---|---:|---:|---:|---|\n| (stage) | stage outcome changed | n/a | 0.000 | 0.000 | yes |\n| `prompt_injection` | action changed | n/a | 0.000 | 0.000 | yes |\n| `prompt_injection` | ROC AUC | 1.000 | 1.000 | 0.000 | yes |\n", markdown, StringComparison.Ordinal);
        Assert.EndsWith("Within tolerance: no number moved more than 0.050.\n", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Deltas_are_signed_and_the_tolerance_is_inclusive()
    {
        var up = ComparedNumber.Pair("p", "x", 0.90, 0.95);
        var down = ComparedNumber.Pair("p", "x", 0.95, 0.90);

        Assert.Equal(0.05, up.Delta!.Value, 12);
        Assert.Equal(-0.05, down.Delta!.Value, 12);
        Assert.True(up.Within(0.05));
        Assert.True(down.Within(0.05));
        Assert.False(up.Within(0.04));
        Assert.Equal(0, ComparedNumber.Pair("p", "x", null, null).Delta);
    }
}

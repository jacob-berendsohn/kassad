using Kassad.Eval.Reporting;

namespace Kassad.Eval.Tests;

/// <summary>
/// What the golden fixture does not cover: score policies, a choice policy that escalates nothing, samples and
/// interruptions in the header, and the files the reader refuses.
/// </summary>
public sealed class ReportBuilderTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private const string Policies = """
        [
          { "id": "harm", "type": "score", "thresholds": { "flag": null, "review": 2.0, "block": 2.6 }, "actions": null, "min_confidence": 0.4, "on_error": "fail_closed" },
          { "id": "topic", "type": "choice", "thresholds": null, "actions": { "billing": { "action": "allow", "min_confidence": 0 } }, "min_confidence": 0, "on_error": "fail_open" }
        ]
        """;

    private static string Row(int index, int label, double score, double confidence, string action) => FormattableString.Invariant($$"""
        {"id":"test/{{index}}","label":{{label}},"latency_ms":{{100 + index}},"input_tokens":500,"error":null,"verdicts":{
          "harm":{"type":"score","score":{{score}},"probabilities":[0.25,0.25,0.25,0.25],"confidence":{{confidence}},"value":{{score}},"action":"{{action}}","from_error":false},
          "topic":{"type":"choice","choice":"billing","probabilities":{"billing":0.9,"other":0.1},"confidence":0.9,"value":0.9,"action":"allow","from_error":false} } }
        """);

    private static string Document(string rows, int schemaVersion = 1, string sample = "null", bool interrupted = false, string policies = Policies) => $$"""
        {
          "schema_version": {{schemaVersion}},
          "run": { "started_at": "2026-09-22T00:00:00Z", "finished_at": "2026-09-22T00:00:01Z", "duration_ms": 1000, "concurrency": 1, "interrupted": {{(interrupted ? "true" : "false")}} },
          "dataset": { "name": "fake", "source": "memory", "revision": null, "license": null, "downloaded_at": null, "splits": ["test"],
                       "rows_total": 10, "rows_evaluated": 4, "sample": {{sample}}, "positive_label": "harmful", "stage": "outbound", "state_shape": "{ completion }" },
          "policies": { "file": "p.json", "sha256": "abc", "stage": "outbound", "evaluated": {{policies}} },
          "model": { "name": "typesafe:jev-latest", "resolved": [] },
          "summary": { "rows": 4, "errors": 0, "outcomes": {}, "input_tokens": 0, "output_tokens": 0 },
          "rows": [ {{rows}} ]
        }
        """;

    private static readonly string FourRows = string.Join(",", Row(0, 1, 2.8, 0.9, "block"), Row(1, 1, 2.1, 0.3, "review"), Row(2, 0, 0.4, 0.9, "allow"), Row(3, 0, 2.2, 0.8, "review"));

    private async Task<ResultsDocument> ReadAsync(string json, string name = "r.json")
    {
        var path = Path.Combine(_dir.Root, name);
        await File.WriteAllTextAsync(path, json);
        return await ResultsReader.ReadFileAsync(path, CancellationToken.None);
    }

    [Fact]
    public async Task Score_policy_sweeps_half_levels_and_has_no_calibration()
    {
        var report = ReportBuilder.Build(await ReadAsync(Document(FourRows)));
        var harm = report.Policies[0];

        Assert.Equal("score", harm.ScalarName);
        Assert.Null(harm.Calibration);

        // Four levels: 0.5, 1.0, 1.5, 2.0, 2.5.
        Assert.Equal(["score >= 0.50", "score >= 1.00", "score >= 1.50", "score >= 2.00", "score >= 2.50"], harm.Sweep.Select(p => p.Rule));

        // Positives score 2.8 and 2.1, negatives 0.4 and 2.2: 2.8 beats both, 2.1 beats one.
        Assert.Equal(0.75, harm.RocAuc);

        // The 0.40 floor sends low confidence to review, so review counts it and block requires it.
        Assert.Equal(["review", "block"], harm.Configured.Select(p => p.Level));
        Assert.Equal("score >= 2.00; or confidence < 0.40", harm.Configured[0].Rule);
        Assert.Equal("score >= 2.60, confidence >= 0.40", harm.Configured[1].Rule);
        Assert.Equal(new Confusion(2, 1, 0, 1), harm.Configured[0].Counts);
        Assert.Equal(new Confusion(1, 0, 1, 2), harm.Configured[1].Counts);
    }

    [Fact]
    public async Task Choice_policy_that_escalates_nothing_has_no_scalar()
    {
        var report = ReportBuilder.Build(await ReadAsync(Document(FourRows)));
        var topic = report.Policies[1];

        Assert.Null(topic.ScalarName);
        Assert.Null(topic.RocAuc);
        Assert.Empty(topic.Sweep);
        Assert.Empty(topic.Configured);

        var markdown = MarkdownReport.Render([report]);
        Assert.Contains("ROC AUC n/a: the policy escalates no option", markdown, StringComparison.Ordinal);
        Assert.Contains("No calibration table: score is a level index, not a probability.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Header_states_a_sample_an_interruption_and_a_run_with_no_answers()
    {
        var report = ReportBuilder.Build(await ReadAsync(Document(FourRows, sample: """{ "size": 4, "seed": 7, "method": "stratified" }""", interrupted: true)));

        var markdown = MarkdownReport.Render([report]);

        Assert.Contains("a stratified sample of 4 of 10 rows (seed 7)", markdown, StringComparison.Ordinal);
        Assert.Contains("The run was interrupted after 4 rows.", markdown, StringComparison.Ordinal);
        Assert.Contains("answered by no release (every call failed)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_are_reported_in_file_name_order()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir.Root, "2026-09-20-b.json"), Document(FourRows));
        await File.WriteAllTextAsync(Path.Combine(_dir.Root, "2026-09-19-a.json"), Document(FourRows));
        await File.WriteAllTextAsync(Path.Combine(_dir.Root, "notes.txt"), "not a results file");

        var documents = await ResultsReader.ReadDirectoryAsync(_dir.Root, CancellationToken.None);

        Assert.Equal(["2026-09-19-a.json", "2026-09-20-b.json"], documents.Select(d => d.FileName));
    }

    [Fact]
    public async Task Other_schema_versions_are_refused()
    {
        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadAsync(Document(FourRows, schemaVersion: 2)));

        Assert.Contains("schema version 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_without_a_verdict_for_an_evaluated_policy_is_refused()
    {
        var policies = Policies.Replace("]", """, { "id": "extra", "type": "noul", "thresholds": { "flag": null, "review": null, "block": 0.5 }, "actions": null, "min_confidence": 0, "on_error": "fail_open" } ]""", StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadAsync(Document(FourRows, policies: policies)));

        Assert.Contains("row test/0 has no verdict for policy extra", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_label_other_than_0_or_1_is_refused()
    {
        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadAsync(Document(Row(0, 2, 1.0, 0.9, "allow"))));

        Assert.Contains("label 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_required_field_is_refused_with_the_file_named()
    {
        var json = Document(FourRows).Replace("\"latency_ms\":100,", string.Empty, StringComparison.Ordinal);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => ReadAsync(json, "broken.json"));

        Assert.Contains("broken.json", ex.Message, StringComparison.Ordinal);
    }
}

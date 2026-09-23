using System.Globalization;
using System.Text;
using Kassad.Eval.Results;

namespace Kassad.Eval.Reporting;

/// <summary>
/// Compares a fresh run against a committed one over the same rows (roadmap 4.4): the numbers the README publishes
/// (ROC AUC, precision and recall at each configured level) plus the share of rows whose verdicts changed, each with
/// an absolute tolerance. Rows are joined by <c>id</c> and must carry the same text (and passage) hash and label, so a
/// difference is the model's, not the sample's; rows whose model call failed on either side are left out and counted.
/// The scheduled <c>eval.yml</c> job runs it and fails on drift.
/// </summary>
internal static class RunComparison
{
    /// <summary>
    /// Largest absolute move any number may make. Re-running the same rows one to five days apart on 2026-09-23, same
    /// model release, changed the answer on a quarter to a half of the rows (usually by 0.01, at most 0.1), flipped the
    /// stage outcome on 1.2% to 1.5% of them and moved the published rates by at most 0.011, so 0.05 sits about five
    /// times above that noise while a change in the model that moves a published rate by five points still fails.
    /// </summary>
    public const double DefaultTolerance = 0.05;

    /// <summary>The label of the file-level row (the stage outcome is not one policy's).</summary>
    public const string StageLabel = "(stage)";

    public static ComparisonResult Compare(ResultsDocument baseline, ResultsDocument candidate, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        RequireSameSetup(baseline, candidate);

        var baselineRows = baseline.Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var matched = new List<(ReportRow Baseline, ReportRow Candidate)>(candidate.Rows.Count);
        var baselineErrors = 0;
        var candidateErrors = 0;
        foreach (var row in candidate.Rows)
        {
            if (!baselineRows.TryGetValue(row.Id, out var other))
            {
                throw new EvalUsageException($"{candidate.FileName}: row {row.Id} is not in {baseline.FileName}; the two runs are not over the same rows.");
            }

            if (row.TextSha256 is null || other.TextSha256 is null)
            {
                throw new EvalUsageException($"row {row.Id} has no text_sha256 in one of the files, so the rows cannot be matched.");
            }

            if (row.TextSha256 != other.TextSha256 || row.PassageSha256 != other.PassageSha256 || row.Label != other.Label)
            {
                throw new EvalUsageException($"{candidate.FileName}: row {row.Id} differs from the same row in {baseline.FileName} (text, passage or label); the two runs are not over the same rows.");
            }

            if (other.Error is not null)
            {
                baselineErrors++;
                continue;
            }

            if (row.Error is not null)
            {
                candidateErrors++;
                continue;
            }

            matched.Add((other, row));
        }

        var numbers = new List<ComparedNumber>();
        var baselineReport = ReportBuilder.Build(baseline with { Rows = matched.Select(m => m.Baseline).ToArray() });
        var candidateReport = ReportBuilder.Build(candidate with { Rows = matched.Select(m => m.Candidate).ToArray() });

        numbers.Add(ComparedNumber.Share(StageLabel, "stage outcome changed", Share(matched, m => Outcome(m.Baseline) != Outcome(m.Candidate))));
        for (var i = 0; i < baselineReport.Policies.Count; i++)
        {
            var b = baselineReport.Policies[i];
            var c = candidateReport.Policies[i];
            numbers.Add(ComparedNumber.Share(b.Id, "action changed", Share(matched, m => m.Baseline.Verdicts[b.Id].Action != m.Candidate.Verdicts[b.Id].Action)));
            if (b.ScalarName is not null)
            {
                numbers.Add(ComparedNumber.Pair(b.Id, "ROC AUC", b.RocAuc, c.RocAuc));
            }

            for (var j = 0; j < b.Configured.Count; j++)
            {
                numbers.Add(ComparedNumber.Pair(b.Id, $"`{b.Configured[j].Level}` precision", b.Configured[j].Counts.Precision, c.Configured[j].Counts.Precision));
                numbers.Add(ComparedNumber.Pair(b.Id, $"`{b.Configured[j].Level}` recall", b.Configured[j].Counts.Recall, c.Configured[j].Counts.Recall));
            }
        }

        return new ComparisonResult(
            baseline.Dataset.Name,
            baseline.FileName,
            candidate.FileName,
            baseline.Model.Resolved,
            candidate.Model.Resolved,
            string.Equals(baseline.Policies.Sha256, candidate.Policies.Sha256, StringComparison.OrdinalIgnoreCase),
            candidate.Rows.Count,
            matched.Count,
            baselineErrors,
            candidateErrors,
            tolerance,
            numbers);
    }

    /// <summary>Same dataset, same stage, and the same policies with the same settings, so the configured levels line up.</summary>
    private static void RequireSameSetup(ResultsDocument baseline, ResultsDocument candidate)
    {
        if (baseline.Dataset.Name != candidate.Dataset.Name)
        {
            throw new EvalUsageException($"{baseline.FileName} is a {baseline.Dataset.Name} run and {candidate.FileName} a {candidate.Dataset.Name} run; compare runs of one dataset.");
        }

        if (baseline.Policies.Stage != candidate.Policies.Stage)
        {
            throw new EvalUsageException($"{baseline.FileName} evaluated the {baseline.Policies.Stage} stage and {candidate.FileName} the {candidate.Policies.Stage} stage.");
        }

        var b = baseline.Policies.Evaluated;
        var c = candidate.Policies.Evaluated;
        if (b.Count != c.Count || b.Zip(c).Any(pair => !SameSettings(pair.First, pair.Second)))
        {
            throw new EvalUsageException(
                $"{baseline.FileName} and {candidate.FileName} were run with different policy settings ({Describe(b)} against {Describe(c)}); the configured operating points would not compare.");
        }
    }

    private static bool SameSettings(PolicyInfo a, PolicyInfo b) =>
        a.Id == b.Id
        && a.Type == b.Type
        && a.MinConfidence == b.MinConfidence
        && a.Thresholds == b.Thresholds
        && SameActions(a.Actions, b.Actions);

    private static bool SameActions(IReadOnlyDictionary<string, ActionInfo>? a, IReadOnlyDictionary<string, ActionInfo>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && other == kv.Value);
    }

    private static string Describe(IReadOnlyList<PolicyInfo> policies) =>
        string.Join(", ", policies.Select(p => p.Thresholds is { } t
            ? Invariant($"{p.Id} {p.Type} flag {t.Flag?.ToString(CultureInfo.InvariantCulture) ?? "-"} review {t.Review?.ToString(CultureInfo.InvariantCulture) ?? "-"} block {t.Block?.ToString(CultureInfo.InvariantCulture) ?? "-"} min_confidence {p.MinConfidence}")
            : Invariant($"{p.Id} {p.Type} actions {string.Join(" ", (p.Actions ?? new Dictionary<string, ActionInfo>()).Select(kv => $"{kv.Key}={kv.Value.Action}@{kv.Value.MinConfidence}"))} min_confidence {p.MinConfidence}")));

    /// <summary>The stage outcome as the runner computed it: the most severe action across the row's verdicts.</summary>
    private static string Outcome(ReportRow row) =>
        row.Verdicts.Values.Select(v => v.Action).OrderByDescending(ReportBuilder.Severity).First();

    private static double? Share(List<(ReportRow Baseline, ReportRow Candidate)> rows, Func<(ReportRow Baseline, ReportRow Candidate), bool> predicate) =>
        rows.Count == 0 ? null : rows.Count(predicate) / (double)rows.Count;

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}

/// <summary>What <see cref="RunComparison.Compare"/> found.</summary>
/// <param name="Dataset">The dataset both files are runs of.</param>
/// <param name="BaselineFile">The committed run's file name.</param>
/// <param name="CandidateFile">The fresh run's file name.</param>
/// <param name="BaselineReleases">Model releases that answered the baseline.</param>
/// <param name="CandidateReleases">Model releases that answered the candidate.</param>
/// <param name="SamePolicyFile">Whether both runs record the same policy file hash (the settings are the same either way).</param>
/// <param name="CandidateRows">Rows in the candidate.</param>
/// <param name="MatchedRows">Candidate rows matched with an answered baseline row and answered themselves: the rows every number is over.</param>
/// <param name="BaselineErrorRows">Matched rows left out because the baseline's model call failed.</param>
/// <param name="CandidateErrorRows">Matched rows left out because the candidate's model call failed.</param>
/// <param name="Tolerance">Largest absolute move allowed.</param>
/// <param name="Numbers">Every compared number, file-level first, then per policy in file order.</param>
internal sealed record ComparisonResult(
    string Dataset,
    string BaselineFile,
    string CandidateFile,
    IReadOnlyList<string> BaselineReleases,
    IReadOnlyList<string> CandidateReleases,
    bool SamePolicyFile,
    int CandidateRows,
    int MatchedRows,
    int BaselineErrorRows,
    int CandidateErrorRows,
    double Tolerance,
    IReadOnlyList<ComparedNumber> Numbers)
{
    /// <summary>The numbers that moved more than the tolerance.</summary>
    public IReadOnlyList<ComparedNumber> Drifted => Numbers.Where(n => !n.Within(Tolerance)).ToArray();

    /// <summary>True when no number moved more than the tolerance.</summary>
    public bool WithinTolerance => Drifted.Count == 0;

    /// <summary>The comparison as GitHub markdown, ASCII only, LF, for the job log and the step summary.</summary>
    public string ToMarkdown()
    {
        var md = new StringBuilder();
        md.Append(Invariant($"### Drift check: {Dataset}\n\n"));
        md.Append(Invariant($"Baseline `{BaselineFile}` (answered by {Releases(BaselineReleases)}) against candidate `{CandidateFile}` (answered by {Releases(CandidateReleases)}): "));
        md.Append(Invariant($"{CandidateRows} candidate rows, {MatchedRows} compared; {BaselineErrorRows} baseline and {CandidateErrorRows} candidate error rows left out. "));
        md.Append(SamePolicyFile ? "Same policy file. " : "Different policy file bytes, same policy settings. ");
        md.Append(Invariant($"Tolerance {Tolerance.ToString("0.000", CultureInfo.InvariantCulture)}, absolute.\n\n"));

        md.Append("| Policy | Number | Baseline | Candidate | Delta | Within tolerance |\n");
        md.Append("|---|---|---:|---:|---:|---|\n");
        foreach (var n in Numbers)
        {
            var policy = n.Policy == RunComparison.StageLabel ? n.Policy : $"`{n.Policy}`";
            md.Append(Invariant($"| {policy} | {n.Name} | {Rate(n.Baseline)} | {Rate(n.Candidate)} | {(n.IsShare ? Rate(n.Delta) : Delta(n.Delta))} | {(n.Delta is null ? "n/a" : n.Within(Tolerance) ? "yes" : "no")} |\n"));
        }

        md.Append('\n');
        md.Append(WithinTolerance
            ? Invariant($"Within tolerance: no number moved more than {Tolerance.ToString("0.000", CultureInfo.InvariantCulture)}.\n")
            : Invariant($"DRIFT: {Drifted.Count} {(Drifted.Count == 1 ? "number" : "numbers")} moved more than {Tolerance.ToString("0.000", CultureInfo.InvariantCulture)}: {string.Join("; ", Drifted.Select(d => $"{d.Policy} {d.Name}"))}.\n"));
        return md.ToString();
    }

    private static string Releases(IReadOnlyList<string> releases) => releases.Count == 0 ? "no release" : string.Join(", ", releases.Select(r => $"`{r}`"));

    private static string Rate(double? value) => value is { } v ? v.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

    private static string Delta(double? value) => value is { } v ? v.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "n/a";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}

/// <summary>
/// One compared number. A pair has a baseline and a candidate value and the signed difference between them; a share (how
/// many rows changed) has no baseline and is its own, unsigned, delta. A pair with a value on one side only (precision when nothing
/// crossed the level in one run) has no delta and is not gated; recall and the shares still are.
/// </summary>
internal sealed record ComparedNumber(string Policy, string Name, double? Baseline, double? Candidate, double? Delta, bool IsShare = false)
{
    public static ComparedNumber Pair(string policy, string name, double? baseline, double? candidate) =>
        new(policy, name, baseline, candidate, baseline is { } b && candidate is { } c ? c - b : baseline is null && candidate is null ? 0 : null);

    public static ComparedNumber Share(string policy, string name, double? share) => new(policy, name, null, share, share, IsShare: true);

    /// <summary>True when the number has no delta or its absolute delta is at most <paramref name="tolerance"/>.</summary>
    public bool Within(double tolerance) => Delta is not { } d || Math.Abs(d) <= tolerance + 1e-12;
}

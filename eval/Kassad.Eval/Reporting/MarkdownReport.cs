using System.Globalization;
using System.Text;

namespace Kassad.Eval.Reporting;

/// <summary>
/// Renders <see cref="FileReport"/>s as GitHub-flavored markdown: first a <c>### Summary</c> table with one row per
/// policy per results file, so several datasets read side by side (roadmap 4.3), then one <c>###</c> section per
/// results file and one <c>####</c> section per policy, so the output can sit under the README's <c>## Numbers</c>
/// heading (roadmap 4.4). Deterministic: no timestamps of its own, invariant culture, LF line endings, so the same
/// files always render the same bytes. Rates to three decimals, latency to one, cost to four.
/// </summary>
internal static class MarkdownReport
{
    private const string NotApplicable = "n/a";

    public static string Render(IReadOnlyList<FileReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var md = new StringBuilder();
        RenderSummary(md, reports);
        foreach (var report in reports)
        {
            RenderFile(md, report);
        }

        RenderMethod(md);
        return md.ToString();
    }

    /// <summary>
    /// One table over every file, a row per policy: the numbers the README leads with, taken from the per-file sections
    /// below (the operating point is the policy's highest configured level and its row in the policy's table).
    /// </summary>
    private static void RenderSummary(StringBuilder md, IReadOnlyList<FileReport> reports)
    {
        md.Append("### Summary\n\n");
        md.Append("| Dataset | File | Stage | Policy | Rows scored | ROC AUC | Operating point | Precision | Recall | Latency p50 / p95 | Cost per 1k checks |\n");
        md.Append("|---|---|---|---|---:|---:|---|---:|---:|---:|---:|\n");
        foreach (var report in reports)
        {
            var doc = report.Document;
            var latency = report.LatencyP50Ms is null ? NotApplicable : Invariant($"{Fixed(report.LatencyP50Ms, "0.0")} / {Fixed(report.LatencyP95Ms, "0.0")} ms");
            foreach (var policy in report.Policies)
            {
                var point = policy.Configured.Count == 0 ? null : policy.Configured[^1];
                var operating = point is null ? NotApplicable : Invariant($"`{point.Level}`: {Cell(point.Rule)}");
                md.Append(Invariant($"| {Cell(doc.Dataset.Name)} | `{Cell(doc.FileName)}` | `{doc.Dataset.Stage}` | `{policy.Id}` | {policy.Scored} | {Rate(policy.RocAuc)} | {operating} | {Rate(point?.Counts.Precision)} | {Rate(point?.Counts.Recall)} | {latency} | {Usd(report.CostPer1kChecksUsd)} |\n"));
            }
        }

        md.Append("\nOne row per policy per results file, in file order. Rows scored are the rows whose model call succeeded; ROC AUC ranks the policy's scalar over them. ");
        md.Append("The operating point is the highest level the policy is configured to reach, with the rule that puts a row there and that rule's precision and recall, as in the policy's table below. ");
        md.Append("Latency and cost are per check, one request carrying every policy of the stage, so a file's policies share them.\n\n");
    }

    private static void RenderFile(StringBuilder md, FileReport report)
    {
        var doc = report.Document;
        var dataset = doc.Dataset;

        md.Append(Invariant($"### {dataset.Name}: `{doc.FileName}`\n\n"));

        var coverage = dataset.Sample is { } sample
            ? Invariant($"a stratified sample of {sample.Size} of {dataset.RowsTotal} rows (seed {sample.Seed})")
            : Invariant($"the full set of {dataset.RowsTotal} rows");
        var interrupted = doc.Run.Interrupted ? Invariant($" The run was interrupted after {dataset.RowsEvaluated} rows.") : string.Empty;
        var revision = dataset.Revision is { Length: > 0 } r ? Invariant($" at revision `{(r.Length > 7 ? r[..7] : r)}`") : string.Empty;
        var resolved = doc.Model.Resolved.Count == 0 ? "no release (every call failed)" : string.Join(", ", doc.Model.Resolved.Select(m => $"`{m}`"));
        md.Append(Invariant($"Source: {dataset.Source}{revision}, splits {string.Join(" + ", dataset.Splits)}: {coverage}, label 1 = {dataset.PositiveLabel}.{interrupted} "));
        md.Append(Invariant($"Stage `{dataset.Stage}`, state `{dataset.StateShape}`, policies from `{doc.Policies.File}` (SHA-256 `{Short(doc.Policies.Sha256)}`). "));
        md.Append(Invariant($"Model `{doc.Model.Name}`, answered by {resolved}.\n\n"));

        md.Append(Invariant($"| Rows | Label 1 ({dataset.PositiveLabel}) | Label 0 | Error rows | Latency p50 | Latency p95 | Input tokens per check | Cost per 1k checks |\n"));
        md.Append("|---:|---:|---:|---:|---:|---:|---:|---:|\n");
        md.Append(Invariant($"| {report.Rows} | {report.Positives} | {report.Rows - report.Positives} | {report.ErrorRows} | {Ms(report.LatencyP50Ms)} | {Ms(report.LatencyP95Ms)} | {Fixed(report.MeanInputTokens, "0.0")} | {Usd(report.CostPer1kChecksUsd)} |\n\n"));
        md.Append(Invariant($"Cost per 1k checks is the mean input tokens per check times 1,000 at ${Pricing.UsdPerMillionInputTokens.ToString("0.000", CultureInfo.InvariantCulture)} per 1M input tokens; output tokens are not billed. "));
        md.Append(Invariant($"Rate quoted, verify: [{Pricing.SourceName}]({Pricing.SourceUrl}). "));
        md.Append(Invariant($"A check is one request carrying the file's {doc.Policies.Evaluated.Count} {doc.Policies.Stage} {(doc.Policies.Evaluated.Count == 1 ? "policy" : "policies")}; latency and tokens are per check, over the rows that got an answer.\n\n"));

        foreach (var policy in report.Policies)
        {
            RenderPolicy(md, policy);
        }
    }

    private static void RenderPolicy(StringBuilder md, PolicyReport policy)
    {
        var scalar = policy.ScalarName is null ? string.Empty : Invariant($", scored on {policy.ScalarName}");
        md.Append(Invariant($"#### `{policy.Id}` ({policy.Type}{scalar})\n\n"));

        var excluded = policy.Excluded == 0 ? string.Empty : Invariant($"; {policy.Excluded} error {(policy.Excluded == 1 ? "row" : "rows")} left out");
        var auc = policy.ScalarName is null
            ? "ROC AUC n/a: the policy escalates no option, so it has no scalar to rank"
            : Invariant($"ROC AUC **{Rate(policy.RocAuc)}**");
        md.Append(Invariant($"{auc} over {policy.Scored} rows ({policy.Positives} label 1, {policy.Scored - policy.Positives} label 0{excluded}).\n\n"));

        md.Append("| Operating point | Rule | Precision | Recall | F1 | TP | FP | FN | TN |\n");
        md.Append("|---|---|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (var point in policy.Configured.Concat(policy.Sweep))
        {
            var name = point.Level is null ? "sweep" : $"`{point.Level}` (configured)";
            var c = point.Counts;
            md.Append(Invariant($"| {name} | {Cell(point.Rule)} | {Rate(c.Precision)} | {Rate(c.Recall)} | {Rate(c.F1)} | {c.TruePositives} | {c.FalsePositives} | {c.FalseNegatives} | {c.TrueNegatives} |\n"));
        }

        md.Append('\n');

        if (policy.Calibration is { } bins)
        {
            md.Append(Invariant($"Calibration of {policy.ScalarName} in {bins.Count} equal-width bins:\n\n"));
            md.Append(Invariant($"| {Cell(policy.ScalarName!)} bin | Rows | Mean predicted | Observed rate |\n"));
            md.Append("|---|---:|---:|---:|\n");
            for (var i = 0; i < bins.Count; i++)
            {
                var bin = bins[i];
                var close = i == bins.Count - 1 ? "]" : ")";
                md.Append(Invariant($"| [{bin.Lower:0.0}, {bin.Upper:0.0}{close} | {bin.Rows} | {Rate(bin.MeanPredicted)} | {Rate(bin.ObservedRate)} |\n"));
            }

            md.Append('\n');
        }
        else if (policy.ScalarName is not null)
        {
            md.Append(Invariant($"No calibration table: {policy.ScalarName} is a level index, not a probability.\n\n"));
        }
    }

    private static void RenderMethod(StringBuilder md)
    {
        md.Append("How these numbers are computed: every policy is scored against the dataset's label, whether or not that label is what the policy asks about. ");
        md.Append("Rows whose model call failed are left out and counted. ");
        md.Append("A configured row counts a row as positive when the action the engine resolved in the run is at or above that level, so confidence floors count as they did in production; ");
        md.Append("a sweep row thresholds the policy's scalar (a noul's p(yes), a choice's summed probability of the options its `actions` escalate, a score's weighted level). ");
        md.Append("Precision is n/a when no row crosses; F1 = 2TP / (2TP + FP + FN). ");
        md.Append("ROC AUC is the Mann-Whitney statistic of the scalar, ties counting one half. ");
        md.Append("Calibration bins are equal-width and half-open, the last one closed; empty bins show n/a. ");
        md.Append("Latency is the model call as the engine timed it (client retries included), nearest-rank percentiles. ");
        md.Append("Generated by `kassad-eval report`; do not edit by hand.\n");
    }

    private static string Rate(double? value) => Fixed(value, "0.000");

    private static string Ms(double? value) => value is null ? NotApplicable : Fixed(value, "0.0") + " ms";

    private static string Usd(double? value) => value is null ? NotApplicable : "$" + Fixed(value, "0.0000");

    private static string Fixed(double? value, string format) => value is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : NotApplicable;

    private static string Short(string sha256) => sha256.Length > 8 ? sha256[..8] : sha256;

    /// <summary>Escape the one character that breaks a table cell.</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}

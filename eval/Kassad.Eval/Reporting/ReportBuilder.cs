using System.Globalization;
using System.Text.Json;
using Kassad.Eval.Results;

namespace Kassad.Eval.Reporting;

/// <summary>
/// Computes the numbers for one results file: per-file latency and cost, and per policy the operating points it was
/// configured with, a threshold sweep, ROC AUC and calibration. Rows whose model call failed are left out of every
/// number and counted.
/// </summary>
internal static class ReportBuilder
{
    private const string Allow = "allow";

    private static readonly string[] Levels = ["flag", "review", "block"];

    public static FileReport Build(ResultsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var answered = document.Rows.Where(r => r.Error is null).ToArray();
        var latencies = answered.Select(r => r.LatencyMs).Order().ToArray();
        var tokens = answered.Where(r => r.InputTokens is not null).Select(r => (double)r.InputTokens!.Value).ToArray();
        double? meanTokens = tokens.Length == 0 ? null : tokens.Average();

        return new FileReport(
            document,
            document.Rows.Count,
            document.Rows.Count(r => r.Label == 1),
            document.Rows.Count - answered.Length,
            Metrics.NearestRank(latencies, 0.50),
            Metrics.NearestRank(latencies, 0.95),
            meanTokens,
            meanTokens * 1000 * Pricing.UsdPerMillionInputTokens / 1_000_000,
            document.Policies.Evaluated.Select(p => BuildPolicy(p, document.Rows)).ToArray());
    }

    private static PolicyReport BuildPolicy(PolicyInfo policy, IReadOnlyList<ReportRow> rows)
    {
        var scored = rows.Where(r => !r.Verdicts[policy.Id].FromError).ToArray();
        var excluded = rows.Count - scored.Length;
        var scalar = ScalarFor(policy);

        var configured = ConfiguredLevels(policy)
            .Select(level => new OperatingPoint(
                level,
                RuleText(policy, level),
                Metrics.Count(scored, r => Severity(r.Verdicts[policy.Id].Action) >= Severity(level), r => r.Label)))
            .ToArray();

        if (scalar is null)
        {
            return new PolicyReport(policy.Id, policy.Type, null, scored.Length, scored.Count(r => r.Label == 1), excluded, null, configured, [], null);
        }

        var values = scored.Select(r => new Scored(scalar.Read(r.Verdicts[policy.Id]), r.Label)).ToArray();
        var sweep = SweepThresholds(policy, scored)
            .Select(t => new OperatingPoint(null, Invariant($"{scalar.Name} >= {Threshold(t)}"), Metrics.Count(values, v => v.Value >= t, v => v.Label)))
            .ToArray();

        return new PolicyReport(
            policy.Id,
            policy.Type,
            scalar.Name,
            scored.Length,
            scored.Count(r => r.Label == 1),
            excluded,
            Metrics.RocAuc(values),
            configured,
            sweep,
            scalar.IsProbability ? Metrics.Calibration(values) : null);
    }

    /// <summary>
    /// The value a policy's thresholds (or, for a choice, its escalating options) act on: a noul's p(yes); for a choice,
    /// the summed probability of the options whose <c>actions</c> entry is not <c>allow</c>; a score's weighted level.
    /// <c>null</c> for a choice that escalates nothing.
    /// </summary>
    private static Scalar? ScalarFor(PolicyInfo policy)
    {
        switch (policy.Type)
        {
            case "noul":
                return new Scalar("p(yes)", true, v => v.Probability ?? throw Missing(policy, "probability"));
            case "score":
                return new Scalar("score", false, v => v.Score ?? throw Missing(policy, "score"));
            case "choice":
                var options = (policy.Actions ?? new Dictionary<string, ActionInfo>())
                    .Where(kv => kv.Value.Action != Allow)
                    .Select(kv => kv.Key)
                    .ToArray();
                if (options.Length == 0)
                {
                    return null;
                }

                var name = string.Join(" + ", options.Select(o => $"p({o})"));
                return new Scalar(name, true, v => options.Sum(o => OptionProbability(policy, v, o)));
            default:
                throw new EvalUsageException($"policy {policy.Id} has question type '{policy.Type}', which the report does not know.");
        }
    }

    private static double OptionProbability(PolicyInfo policy, ReportVerdict verdict, string option)
    {
        if (verdict.Probabilities is not { ValueKind: JsonValueKind.Object } probabilities)
        {
            throw Missing(policy, "probabilities");
        }

        return probabilities.TryGetProperty(option, out var p) ? p.GetDouble() : 0;
    }

    private static EvalUsageException Missing(PolicyInfo policy, string field) =>
        new($"a verdict of policy {policy.Id} ({policy.Type}) has no {field}.");

    /// <summary>
    /// Probability scalars sweep 0.1 to 0.9 in steps of 0.1. A score sweeps half levels from 0.5 to one half below its top
    /// level, the level count read from the rows' probability arrays.
    /// </summary>
    private static double[] SweepThresholds(PolicyInfo policy, IReadOnlyList<ReportRow> scored)
    {
        if (policy.Type != "score")
        {
            return Enumerable.Range(1, 9).Select(i => i / 10.0).ToArray();
        }

        var levels = scored
            .Select(r => r.Verdicts[policy.Id].Probabilities)
            .Where(p => p is { ValueKind: JsonValueKind.Array })
            .Select(p => p!.Value.GetArrayLength())
            .DefaultIfEmpty(0)
            .Max();
        return Enumerable.Range(1, Math.Max((2 * levels) - 3, 0)).Select(i => i / 2.0).ToArray();
    }

    /// <summary>
    /// The levels a policy can resolve to above <c>allow</c>, in severity order: those with a threshold (noul, score) or an
    /// <c>actions</c> entry (choice), plus <c>review</c> when a confidence floor can send a verdict there.
    /// </summary>
    private static string[] ConfiguredLevels(PolicyInfo policy)
    {
        var levels = new HashSet<string>(StringComparer.Ordinal);
        if (policy.Thresholds is { } t)
        {
            AddIf(levels, "flag", t.Flag is not null);
            AddIf(levels, "review", t.Review is not null);
            AddIf(levels, "block", t.Block is not null);
        }

        foreach (var rule in policy.Actions?.Values ?? [])
        {
            AddIf(levels, rule.Action, rule.Action != Allow);
            AddIf(levels, "review", rule.MinConfidence > 0);
        }

        // Noul answers carry no confidence, so the floor never applies to them.
        AddIf(levels, "review", policy.MinConfidence > 0 && policy.Type != "noul");
        return Levels.Where(levels.Contains).ToArray();
    }

    private static void AddIf(HashSet<string> set, string value, bool condition)
    {
        if (condition)
        {
            set.Add(value);
        }
    }

    /// <summary>What "action at or above <paramref name="level"/>" meant under the policy's configuration, in words.</summary>
    private static string RuleText(PolicyInfo policy, string level)
    {
        var parts = new List<string>();
        var atMostReview = Severity(level) <= Severity("review");

        if (policy.Type == "choice")
        {
            foreach (var (option, rule) in policy.Actions ?? new Dictionary<string, ActionInfo>())
            {
                var floor = Math.Max(rule.MinConfidence, policy.MinConfidence);
                if (Severity(rule.Action) >= Severity(level))
                {
                    parts.Add(!atMostReview && floor > 0 ? Invariant($"chose {option}, confidence >= {Threshold(floor)}") : $"chose {option}");
                }
                else if (atMostReview && rule.MinConfidence > 0)
                {
                    parts.Add(Invariant($"chose {option}, confidence < {Threshold(rule.MinConfidence)}"));
                }
            }

            if (atMostReview && policy.MinConfidence > 0)
            {
                parts.Add(Invariant($"confidence < {Threshold(policy.MinConfidence)}"));
            }

            return parts.Count == 0 ? "never" : string.Join("; or ", parts);
        }

        var name = policy.Type == "noul" ? "p(yes)" : "score";
        var threshold = LowestThresholdAtOrAbove(policy.Thresholds, level);
        var floorApplies = policy.Type != "noul" && policy.MinConfidence > 0;
        if (threshold is { } value)
        {
            parts.Add(Invariant($"{name} >= {Threshold(value)}"));
        }

        if (floorApplies && atMostReview)
        {
            parts.Add(Invariant($"confidence < {Threshold(policy.MinConfidence)}"));
            return string.Join("; or ", parts);
        }

        if (floorApplies && parts.Count > 0)
        {
            return parts[0] + Invariant($", confidence >= {Threshold(policy.MinConfidence)}");
        }

        return parts.Count == 0 ? "never" : parts[0];
    }

    private static double? LowestThresholdAtOrAbove(ThresholdsInfo? thresholds, string level)
    {
        if (thresholds is null)
        {
            return null;
        }

        double?[] byLevel = [thresholds.Flag, thresholds.Review, thresholds.Block];
        return byLevel.Skip(Severity(level) - 1).Where(t => t is not null).Min();
    }

    /// <summary>0 for allow up to 3 for block; unknown spellings are refused.</summary>
    private static int Severity(string action) => action switch
    {
        "allow" => 0,
        "flag" => 1,
        "review" => 2,
        "block" => 3,
        _ => throw new EvalUsageException($"unknown action '{action}' in a results file."),
    };

    /// <summary>A threshold with at least two decimals and up to four: 0.4 → 0.40, 0.85 → 0.85, 2.6 → 2.60.</summary>
    private static string Threshold(double value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private sealed record Scalar(string Name, bool IsProbability, Func<ReportVerdict, double> Read);
}

/// <summary>The numbers for one results file.</summary>
/// <param name="Document">The file.</param>
/// <param name="Rows">Rows in the file.</param>
/// <param name="Positives">Rows labeled 1.</param>
/// <param name="ErrorRows">Rows whose model call failed; left out of every number below.</param>
/// <param name="LatencyP50Ms">Nearest-rank median of the answered rows' model latency.</param>
/// <param name="LatencyP95Ms">Nearest-rank 95th percentile of the same.</param>
/// <param name="MeanInputTokens">Mean input tokens per check (one request carries every policy of the stage).</param>
/// <param name="CostPer1kChecksUsd">Mean input tokens × 1,000 × <see cref="Pricing.UsdPerMillionInputTokens"/> / 10⁶.</param>
/// <param name="Policies">One entry per policy evaluated, in file order.</param>
internal sealed record FileReport(
    ResultsDocument Document,
    int Rows,
    int Positives,
    int ErrorRows,
    double? LatencyP50Ms,
    double? LatencyP95Ms,
    double? MeanInputTokens,
    double? CostPer1kChecksUsd,
    IReadOnlyList<PolicyReport> Policies);

/// <summary>The numbers for one policy over one file.</summary>
/// <param name="Id">Policy id.</param>
/// <param name="Type"><c>noul</c>, <c>choice</c> or <c>score</c>.</param>
/// <param name="ScalarName">What the sweep, AUC and calibration act on, e.g. <c>p(yes)</c>; <c>null</c> when the policy has none.</param>
/// <param name="Scored">Rows scored (answered).</param>
/// <param name="Positives">Scored rows labeled 1.</param>
/// <param name="Excluded">Rows left out because the model call failed.</param>
/// <param name="RocAuc">ROC AUC of the scalar, or <c>null</c>.</param>
/// <param name="Configured">One point per configured level: predicted positive when the recorded action is at or above it.</param>
/// <param name="Sweep">One point per sweep threshold: predicted positive when the scalar is at or above it.</param>
/// <param name="Calibration">Ten bins, for probability scalars only.</param>
internal sealed record PolicyReport(
    string Id,
    string Type,
    string? ScalarName,
    int Scored,
    int Positives,
    int Excluded,
    double? RocAuc,
    IReadOnlyList<OperatingPoint> Configured,
    IReadOnlyList<OperatingPoint> Sweep,
    IReadOnlyList<CalibrationBin>? Calibration);

/// <summary>An operating point: the configured level it stands for (<c>null</c> for a sweep point), its rule in words and its counts.</summary>
internal sealed record OperatingPoint(string? Level, string Rule, Confusion Counts);

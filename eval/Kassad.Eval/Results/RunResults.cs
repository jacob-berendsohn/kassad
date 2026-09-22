using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Kassad.Eval.Datasets;
using Kassad.Policies;

namespace Kassad.Eval.Results;

/// <summary>
/// The document <c>kassad-eval run</c> writes. Property names become snake_case JSON names, and
/// <c>eval/README.md</c> documents every one of them: a rename here is a schema change and bumps
/// <see cref="SchemaVersion"/>. Dataset text never appears in it (raw data is never committed); rows carry the
/// text's hash and length instead, and <see cref="RowResult.Id"/> joins them back to the source.
/// </summary>
internal sealed record RunResults
{
    public int SchemaVersion { get; init; } = 1;

    public required HarnessInfo Harness { get; init; }

    public required RunInfo Run { get; init; }

    public required DatasetInfo Dataset { get; init; }

    public required PoliciesInfo Policies { get; init; }

    public required ModelInfo Model { get; init; }

    public required RunSummary Summary { get; init; }

    /// <summary>
    /// One entry per evaluated input, in dataset order. <see cref="ResultsWriter"/> writes them after the envelope, one per
    /// line. Not <c>required</c>: System.Text.Json rejects a required property it is told to ignore.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<RowResult> Rows { get; init; } = [];
}

/// <summary>Which build of the harness wrote the file.</summary>
/// <param name="Name">Always <c>kassad-eval</c>.</param>
/// <param name="Version">The assembly's informational version (MinVer: version plus commit), or <c>null</c> when unknown.</param>
internal sealed record HarnessInfo(string Name, string? Version);

/// <summary>When and how the run happened.</summary>
/// <param name="StartedAt">UTC, just before the first request.</param>
/// <param name="FinishedAt">UTC, after the last request.</param>
/// <param name="DurationMs">Wall-clock time between the two.</param>
/// <param name="Concurrency">Rows in flight at once.</param>
/// <param name="Interrupted">True when the run was cancelled: <see cref="DatasetInfo.RowsEvaluated"/> is then short of the set.</param>
internal sealed record RunInfo(DateTime StartedAt, DateTime FinishedAt, long DurationMs, int Concurrency, bool Interrupted);

/// <summary>What was evaluated.</summary>
/// <param name="Name">The <c>--dataset</c> name.</param>
/// <param name="Source">Where the data comes from.</param>
/// <param name="Revision">The source's revision at download time, when the download script recorded one.</param>
/// <param name="License">The license the source declares, when recorded.</param>
/// <param name="DownloadedAt">When the download script ran, UTC, when recorded.</param>
/// <param name="Splits">The splits included, in row order.</param>
/// <param name="RowsTotal">Rows in the downloaded set.</param>
/// <param name="RowsEvaluated">Rows in this file: the set, the sample, or fewer after an interruption.</param>
/// <param name="Sample">How the rows were sampled, or <c>null</c> for the full set.</param>
/// <param name="PositiveLabel">What <c>label = 1</c> means.</param>
/// <param name="Stage">The stage evaluated, in the policy-file spelling.</param>
/// <param name="StateShape">How each row's text reached the model.</param>
internal sealed record DatasetInfo(
    string Name,
    string Source,
    string? Revision,
    string? License,
    DateTime? DownloadedAt,
    IReadOnlyList<string> Splits,
    int RowsTotal,
    int RowsEvaluated,
    SampleInfo? Sample,
    string PositiveLabel,
    string Stage,
    string StateShape);

/// <summary>A run over a sample rather than the full set.</summary>
internal sealed record SampleInfo(int Size, int Seed, string Method);

/// <summary>The policy file and the policies that were evaluated from it.</summary>
/// <param name="File">The path as given on the command line, with forward slashes.</param>
/// <param name="Sha256">Hex SHA-256 of the file's bytes, so a later report can tell which thresholds these rows were judged against.</param>
/// <param name="Stage">The stage whose policies ran; policies for other stages were ignored.</param>
/// <param name="Evaluated">The policies of that stage, in file order, with the settings that decide their actions.</param>
internal sealed record PoliciesInfo(string File, string Sha256, string Stage, IReadOnlyList<PolicyInfo> Evaluated);

/// <summary>One policy's settings, as the policy file spells them.</summary>
internal sealed record PolicyInfo(string Id, string Type, ThresholdsInfo? Thresholds, IReadOnlyDictionary<string, ActionInfo>? Actions, double MinConfidence, string OnError)
{
    public static PolicyInfo From(Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new PolicyInfo(
            policy.Id,
            Names.QuestionTypeName(policy.Question),
            policy.Thresholds is { } t ? new ThresholdsInfo(t.Flag, t.Review, t.Block) : null,
            policy.ChoiceRules?.ToDictionary(kv => kv.Key, kv => new ActionInfo(Names.ActionName(kv.Value.Action), kv.Value.MinConfidence), StringComparer.Ordinal),
            policy.MinConfidence,
            Names.OnErrorName(policy.OnError));
    }
}

/// <summary>Cut points of a noul or score policy; a missing one is <c>null</c>.</summary>
internal sealed record ThresholdsInfo(double? Flag, double? Review, double? Block);

/// <summary>One <c>actions</c> entry of a choice policy.</summary>
internal sealed record ActionInfo(string Action, double MinConfidence);

/// <summary>The decision model.</summary>
/// <param name="Name"><see cref="IDecisionModel.Name"/>: the alias sent, e.g. <c>typesafe:jev-latest</c>.</param>
/// <param name="Resolved">Every distinct release that answered during the run (<see cref="DecisionResponse.Model"/>), sorted; usually one.</param>
internal sealed record ModelInfo(string Name, IReadOnlyList<string> Resolved);

/// <summary>Counts over the rows in the file. Metrics come from the rows themselves (roadmap 4.2), not from here.</summary>
/// <param name="Rows">Rows in the file.</param>
/// <param name="Errors">Rows whose verdicts came from the policies' <c>on_error</c> because the model failed.</param>
/// <param name="Outcomes">Rows per stage outcome, every action listed.</param>
/// <param name="InputTokens">Sum of <see cref="RowResult.InputTokens"/> over rows that have one.</param>
/// <param name="OutputTokens">Sum of <see cref="RowResult.OutputTokens"/> over rows that have one.</param>
internal sealed record RunSummary(int Rows, int Errors, IReadOnlyDictionary<string, int> Outcomes, long InputTokens, long OutputTokens);

/// <summary>One evaluated input: the stage result of running every policy of the stage over it in one model call.</summary>
internal sealed record RowResult
{
    /// <summary><c>split/index</c>, the source row this came from.</summary>
    public required string Id { get; init; }

    public required string Split { get; init; }

    public required int Index { get; init; }

    /// <summary>1 for the positive class (<see cref="DatasetInfo.PositiveLabel"/>), 0 otherwise.</summary>
    public required int Label { get; init; }

    /// <summary>Hex SHA-256 of the UTF-8 text, so a row can be checked against the source without copying the text.</summary>
    public required string TextSha256 { get; init; }

    /// <summary>Length of the text in UTF-16 characters.</summary>
    public required int TextChars { get; init; }

    /// <summary>Grounding rows: hex SHA-256 of the UTF-8 source passage the claim was judged against; left out for other stages.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PassageSha256 { get; init; }

    /// <summary>Grounding rows: length of the source passage in UTF-16 characters; left out for other stages.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PassageChars { get; init; }

    /// <summary>The stage outcome: the most severe action across the verdicts.</summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// The engine's <see cref="StageResult.ModelLatency"/> in milliseconds, one decimal: the wall-clock time of the model
    /// call including the client's retries, the same measurement the <c>kassad.model.latency</c> histogram records.
    /// </summary>
    public required double LatencyMs { get; init; }

    /// <summary>Tokens the model reported for the request; <c>null</c> when the call failed.</summary>
    public required int? InputTokens { get; init; }

    /// <summary>Tokens the model reported for its answers; <c>null</c> when the call failed.</summary>
    public required int? OutputTokens { get; init; }

    /// <summary>Why the model call failed, from the first error verdict; <c>null</c> when it succeeded.</summary>
    public required string? Error { get; init; }

    /// <summary>One entry per policy of the stage, keyed by policy id, in policy-file order.</summary>
    public required IReadOnlyDictionary<string, VerdictResult> Verdicts { get; init; }

    public static RowResult From(EvalRow row, StageResult result, IReadOnlyDictionary<string, Policy> policies)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(policies);

        var verdicts = new Dictionary<string, VerdictResult>(result.Verdicts.Count, StringComparer.Ordinal);
        string? error = null;
        foreach (var verdict in result.Verdicts)
        {
            verdicts[verdict.PolicyId] = VerdictResult.From(verdict, policies[verdict.PolicyId]);
            if (verdict.FromError)
            {
                error ??= verdict.Reason;
            }
        }

        return new RowResult
        {
            Id = row.Id,
            Split = row.Split,
            Index = row.Index,
            Label = row.Label,
            TextSha256 = Sha256Hex(row.Text),
            TextChars = row.Text.Length,
            PassageSha256 = row.SourcePassage is null ? null : Sha256Hex(row.SourcePassage),
            PassageChars = row.SourcePassage?.Length,
            Outcome = Names.ActionName(result.Outcome),
            LatencyMs = Math.Round(result.ModelLatency.TotalMilliseconds, 1),
            InputTokens = result.Usage?.InputTokens,
            OutputTokens = result.Usage?.OutputTokens,
            Error = error,
            Verdicts = verdicts,
        };
    }

    private static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>
/// One policy's verdict on one row: the answer as the model gave it, the value the thresholds were applied to, and the
/// action that came out. Fields that do not apply to the question type are left out.
/// </summary>
internal sealed record VerdictResult
{
    /// <summary><c>noul</c>, <c>choice</c> or <c>score</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Noul: the probability that the answer is yes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Probability { get; init; }

    /// <summary>Choice: the option the model selected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Choice { get; init; }

    /// <summary>Choice: option to probability, in the policy's option order. Score: probability per level, in level order.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Probabilities { get; init; }

    /// <summary>Score: the probability-weighted level index.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Score { get; init; }

    /// <summary>Choice and score: the model's confidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Confidence { get; init; }

    /// <summary>The value the policy thresholded: the noul probability, the score, or the probability of the selected option (<see cref="Verdict.Value"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Value { get; init; }

    /// <summary>The action the policy resolved to.</summary>
    public required string Action { get; init; }

    /// <summary>True when the action came from the policy's <c>on_error</c> because the model failed; the answer fields are then absent.</summary>
    public required bool FromError { get; init; }

    /// <summary>The failure, when <see cref="FromError"/> is true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    public static VerdictResult From(Verdict verdict, Policy policy)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(policy);

        var type = Names.QuestionTypeName(policy.Question);
        var action = Names.ActionName(verdict.Action);
        return verdict.Answer switch
        {
            NoulAnswer noul => new VerdictResult
            {
                Type = type,
                Probability = noul.Probability,
                Value = verdict.Value,
                Action = action,
                FromError = false,
            },
            ChoiceAnswer choice => new VerdictResult
            {
                Type = type,
                Choice = choice.Choice,
                Probabilities = InOptionOrder(choice, policy),
                Confidence = choice.Confidence,
                Value = verdict.Value,
                Action = action,
                FromError = false,
            },
            ScoreAnswer score => new VerdictResult
            {
                Type = type,
                Score = score.Score,
                Probabilities = score.Probabilities,
                Confidence = score.Confidence,
                Value = verdict.Value,
                Action = action,
                FromError = false,
            },
            _ => new VerdictResult
            {
                Type = type,
                Action = action,
                FromError = verdict.FromError,
                Error = verdict.FromError ? verdict.Reason : null,
            },
        };
    }

    /// <summary>The API returns choice probabilities in arbitrary key order (API notes); the file lists them as the policy declares the options.</summary>
    private static Dictionary<string, double> InOptionOrder(ChoiceAnswer choice, Policy policy)
    {
        var ordered = new Dictionary<string, double>(choice.Probabilities.Count, StringComparer.Ordinal);
        if (policy.Question is ChoiceQuestion question)
        {
            foreach (var option in question.Options.Keys)
            {
                if (choice.Probabilities.TryGetValue(option, out var p))
                {
                    ordered[option] = p;
                }
            }
        }

        foreach (var (option, p) in choice.Probabilities.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            ordered.TryAdd(option, p);
        }

        return ordered;
    }
}

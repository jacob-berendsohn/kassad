namespace Kassad;

/// <summary>Where in an LLM interaction a policy applies.</summary>
public enum Stage
{
    /// <summary>User → LLM. The prompt, message, or request body before it reaches the model.</summary>
    Inbound,

    /// <summary>LLM → user. The completion before it reaches the caller. State includes the originating request.</summary>
    Outbound,

    /// <summary>A tool/function call proposed by the LLM, before it is executed. State includes user intent, tool schema, and arguments.</summary>
    ToolCall,

    /// <summary>A claim paired with the source passage it cites. Checks whether the source supports the claim.</summary>
    Grounding,
}

/// <summary>
/// What the caller should do. Ordered by severity so the outcome of a stage is the maximum over its verdicts.
/// </summary>
public enum VerdictAction
{
    /// <summary>Proceed. No policy fired.</summary>
    Allow = 0,

    /// <summary>Proceed, but record it. Low-cost signal for later analysis.</summary>
    Flag = 1,

    /// <summary>Proceed only with confirmation, or hold for a human. The model is uncertain or the stakes warrant a second look.</summary>
    Review = 2,

    /// <summary>Do not proceed.</summary>
    Block = 3,
}

/// <summary>What a policy does when the decision model cannot be reached or returns garbage. Every policy must declare one; there is no default.</summary>
public enum ErrorPolicy
{
    /// <summary>Treat the check as passed. Availability over safety. Suitable for low-stakes, read-only paths.</summary>
    FailOpen,

    /// <summary>Treat the check as failed and block. Safety over availability. Required for anything destructive.</summary>
    FailClosed,
}

/// <summary>The outcome of one policy against one state.</summary>
public sealed record Verdict
{
    /// <summary>The policy that produced this verdict.</summary>
    public required string PolicyId { get; init; }

    /// <summary>The stage the policy belongs to.</summary>
    public required Stage Stage { get; init; }

    /// <summary>What the caller should do.</summary>
    public required VerdictAction Action { get; init; }

    /// <summary>The raw answer, or <c>null</c> if the model call failed and <see cref="FromError"/> is true.</summary>
    public Answer? Answer { get; init; }

    /// <summary>The value the threshold was applied to: Noul probability, Score value, or Choice probability of the selected option.</summary>
    public double? Value { get; init; }

    /// <summary>Model confidence for Choice/Score answers. Always <c>null</c> for Noul.</summary>
    public double? Confidence { get; init; }

    /// <summary>One line explaining why this action was chosen. For logs, not for end users.</summary>
    public required string Reason { get; init; }

    /// <summary>True when the action came from the policy's <see cref="ErrorPolicy"/> rather than an answer.</summary>
    public bool FromError { get; init; }
}

/// <summary>Every verdict for one stage evaluation, plus the combined outcome.</summary>
public sealed record StageResult
{
    /// <summary>The stage that was evaluated.</summary>
    public required Stage Stage { get; init; }

    /// <summary>One verdict per policy registered for this stage.</summary>
    public required IReadOnlyList<Verdict> Verdicts { get; init; }

    /// <summary>The most severe <see cref="VerdictAction"/> across <see cref="Verdicts"/>.</summary>
    public required VerdictAction Outcome { get; init; }

    /// <summary>Wall-clock time for the model call. Zero if no call was made.</summary>
    public required TimeSpan ModelLatency { get; init; }

    /// <summary>Token usage reported by the model, if the call succeeded.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>True if any verdict came from an error path rather than an answer.</summary>
    public bool HadModelError => Verdicts.Any(v => v.FromError);
}

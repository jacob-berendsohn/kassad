namespace Kassad.Policies;

/// <summary>
/// One check: a question, the stage it runs at, how its answer maps to a <see cref="VerdictAction"/>,
/// and what to do if the model can't answer. Thresholds live here, never in the question text.
/// </summary>
public sealed record Policy
{
    /// <summary>Unique within a <see cref="PolicySet"/>. Used as the question key and in every log line.</summary>
    public required string Id { get; init; }

    /// <summary>Where this policy runs.</summary>
    public required Stage Stage { get; init; }

    /// <summary>The single, narrow question this policy asks.</summary>
    public required Question Question { get; init; }

    /// <summary>Behavior when the decision model fails. Mandatory: there is deliberately no default.</summary>
    public required ErrorPolicy OnError { get; init; }

    /// <summary>
    /// For <see cref="NoulQuestion"/> the thresholds apply to the yes-probability (0–1).
    /// For <see cref="ScoreQuestion"/> they apply to the score value in level units (0 = first level).
    /// Ignored for <see cref="ChoiceQuestion"/>.
    /// </summary>
    public ActionThresholds? Thresholds { get; init; }

    /// <summary>For <see cref="ChoiceQuestion"/>: option key → what to do when that option is chosen. Options without a rule resolve to Allow.</summary>
    public IReadOnlyDictionary<string, ChoiceRule>? ChoiceRules { get; init; }

    /// <summary>
    /// For Choice and Score answers: if the model's confidence is below this floor the verdict is Review
    /// regardless of the answer. 0 disables the floor. Noul answers carry no confidence, so this is ignored for them.
    /// </summary>
    public double MinConfidence { get; init; }

    /// <summary>Free text for humans reading the policy file. Not sent to the model.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Ascending cut points. A value at or above <see cref="Block"/> blocks; else at or above
/// <see cref="Review"/> reviews; else at or above <see cref="Flag"/> flags; else allows.
/// Any cut point may be omitted, but at least one must be present.
/// </summary>
public sealed record ActionThresholds(double? Flag = null, double? Review = null, double? Block = null)
{
    /// <summary>Map a value to an action using the configured cut points.</summary>
    public VerdictAction Resolve(double value)
    {
        if (Block is { } b && value >= b)
        {
            return VerdictAction.Block;
        }

        if (Review is { } r && value >= r)
        {
            return VerdictAction.Review;
        }

        if (Flag is { } f && value >= f)
        {
            return VerdictAction.Flag;
        }

        return VerdictAction.Allow;
    }
}

/// <summary>What to do when a Choice policy selects a particular option.</summary>
/// <param name="Action">The action when the option is chosen with sufficient confidence.</param>
/// <param name="MinConfidence">If the answer's confidence is below this, the verdict is Review instead of <paramref name="Action"/>. 0 means always apply the action.</param>
public sealed record ChoiceRule(VerdictAction Action, double MinConfidence = 0);

using System.Globalization;
using Kassad.Policies;

namespace Kassad.Engine;

/// <summary>Pure mapping from (policy, answer) to a <see cref="Verdict"/>. No I/O, fully unit-testable.</summary>
public static class VerdictResolver
{
    /// <summary>Resolve an answer against its policy.</summary>
    public static Verdict Resolve(Policy policy, Answer answer)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(answer);

        return answer switch
        {
            NoulAnswer noul => ResolveThresholded(policy, answer, noul.Probability, confidence: null, "p(yes)"),
            ScoreAnswer score => ResolveThresholded(policy, answer, score.Score, score.Confidence, "score"),
            ChoiceAnswer choice => ResolveChoice(policy, choice),
            _ => FromError(policy, new DecisionModelException($"Unsupported answer type {answer.GetType().Name}.")),
        };
    }

    /// <summary>Verdict when the model could not be consulted. Applies the policy's <see cref="ErrorPolicy"/>.</summary>
    public static Verdict FromError(Policy policy, Exception error)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(error);

        var action = policy.OnError == ErrorPolicy.FailClosed ? VerdictAction.Block : VerdictAction.Allow;
        return new Verdict
        {
            PolicyId = policy.Id,
            Stage = policy.Stage,
            Action = action,
            Answer = null,
            Reason = $"model error ({error.GetType().Name}: {error.Message}); {Describe(policy.OnError)} → {action}",
            FromError = true,
        };
    }

    private static Verdict ResolveThresholded(Policy policy, Answer answer, double value, double? confidence, string valueName)
    {
        if (policy.Thresholds is null)
        {
            // Unreachable for a validated PolicySet; defensive for hand-built policies.
            return FromError(policy, new InvalidOperationException("Policy has no thresholds."));
        }

        if (confidence is { } c && policy.MinConfidence > 0 && c < policy.MinConfidence)
        {
            return new Verdict
            {
                PolicyId = policy.Id,
                Stage = policy.Stage,
                Action = VerdictAction.Review,
                Answer = answer,
                Value = value,
                Confidence = c,
                Reason = $"confidence {F(c)} below floor {F(policy.MinConfidence)} → Review",
            };
        }

        var action = policy.Thresholds.Resolve(value);
        var crossed = action switch
        {
            VerdictAction.Block => policy.Thresholds.Block,
            VerdictAction.Review => policy.Thresholds.Review,
            VerdictAction.Flag => policy.Thresholds.Flag,
            _ => null,
        };

        return new Verdict
        {
            PolicyId = policy.Id,
            Stage = policy.Stage,
            Action = action,
            Answer = answer,
            Value = value,
            Confidence = confidence,
            Reason = crossed is { } t
                ? $"{valueName} {F(value)} ≥ {action.ToString().ToLowerInvariant()} threshold {F(t)}"
                : $"{valueName} {F(value)} below all thresholds",
        };
    }

    private static Verdict ResolveChoice(Policy policy, ChoiceAnswer choice)
    {
        var selected = choice.Probabilities.TryGetValue(choice.Choice, out var p) ? p : (double?)null;

        if (policy.MinConfidence > 0 && choice.Confidence < policy.MinConfidence)
        {
            return new Verdict
            {
                PolicyId = policy.Id,
                Stage = policy.Stage,
                Action = VerdictAction.Review,
                Answer = choice,
                Value = selected,
                Confidence = choice.Confidence,
                Reason = $"chose '{choice.Choice}' but confidence {F(choice.Confidence)} below floor {F(policy.MinConfidence)} → Review",
            };
        }

        if (policy.ChoiceRules is null || !policy.ChoiceRules.TryGetValue(choice.Choice, out var rule))
        {
            return new Verdict
            {
                PolicyId = policy.Id,
                Stage = policy.Stage,
                Action = VerdictAction.Allow,
                Answer = choice,
                Value = selected,
                Confidence = choice.Confidence,
                Reason = $"chose '{choice.Choice}' (no rule) → Allow",
            };
        }

        if (rule.MinConfidence > 0 && choice.Confidence < rule.MinConfidence)
        {
            return new Verdict
            {
                PolicyId = policy.Id,
                Stage = policy.Stage,
                Action = VerdictAction.Review,
                Answer = choice,
                Value = selected,
                Confidence = choice.Confidence,
                Reason = $"chose '{choice.Choice}' → {rule.Action}, but confidence {F(choice.Confidence)} below rule floor {F(rule.MinConfidence)} → Review",
            };
        }

        return new Verdict
        {
            PolicyId = policy.Id,
            Stage = policy.Stage,
            Action = rule.Action,
            Answer = choice,
            Value = selected,
            Confidence = choice.Confidence,
            Reason = $"chose '{choice.Choice}' with confidence {F(choice.Confidence)} → {rule.Action}",
        };
    }

    private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Describe(ErrorPolicy e) => e == ErrorPolicy.FailClosed ? "fail_closed" : "fail_open";
}

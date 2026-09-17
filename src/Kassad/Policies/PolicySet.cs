using System.Collections.Immutable;

namespace Kassad.Policies;

/// <summary>
/// An immutable, validated set of policies indexed by <see cref="Stage"/>.
/// Construct from code with <see cref="FromPolicies"/> or from a JSON document with <see cref="FromJson"/> / <see cref="FromFile"/>.
/// Construction fails with <see cref="PolicyValidationException"/> listing every problem; a half-valid set never exists.
/// </summary>
public sealed class PolicySet
{
    private readonly ImmutableDictionary<Stage, ImmutableArray<Policy>> _byStage;

    private PolicySet(ImmutableArray<Policy> policies)
    {
        All = policies;
        _byStage = policies
            .GroupBy(p => p.Stage)
            .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
    }

    /// <summary>Every policy, in declaration order.</summary>
    public ImmutableArray<Policy> All { get; }

    /// <summary>Policies registered for <paramref name="stage"/>, or empty.</summary>
    public ImmutableArray<Policy> ForStage(Stage stage) =>
        _byStage.TryGetValue(stage, out var policies) ? policies : ImmutableArray<Policy>.Empty;

    /// <summary>Build and validate from in-memory policies.</summary>
    /// <exception cref="PolicyValidationException">One or more policies are invalid.</exception>
    public static PolicySet FromPolicies(IEnumerable<Policy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var array = policies.ToImmutableArray();
        Validate(array);
        return new PolicySet(array);
    }

    /// <summary>Parse and validate a policy document. See <c>Docs/specs/policy-file-format.md</c> for the schema.</summary>
    /// <exception cref="PolicyValidationException">The document is malformed or a policy is invalid.</exception>
    public static PolicySet FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return FromPolicies(PolicyDocument.Parse(json));
    }

    /// <summary>Read, parse and validate a policy document from disk.</summary>
    public static PolicySet FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path));
    }

    private static void Validate(ImmutableArray<Policy> policies)
    {
        var errors = new List<string>();

        if (policies.Length == 0)
        {
            errors.Add("Policy set contains no policies.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
        {
            var id = policy.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add("A policy has an empty id.");
                continue;
            }

            if (!seen.Add(id))
            {
                errors.Add($"{id}: duplicate policy id (ids are case-insensitive).");
            }

            if (!Enum.IsDefined(policy.Stage))
            {
                errors.Add($"{id}: unknown stage '{policy.Stage}'.");
            }

            if (!Enum.IsDefined(policy.OnError))
            {
                errors.Add($"{id}: on_error must be fail_open or fail_closed.");
            }

            if (policy.MinConfidence is < 0 or > 1)
            {
                errors.Add($"{id}: min_confidence must be between 0 and 1.");
            }

            switch (policy.Question)
            {
                case NoulQuestion:
                    ValidateThresholds(id, policy.Thresholds, min: 0, max: 1, unit: "probability", errors);
                    if (policy.ChoiceRules is not null)
                    {
                        errors.Add($"{id}: 'actions' is only valid for choice policies.");
                    }

                    break;

                case ScoreQuestion score:
                    ValidateThresholds(id, policy.Thresholds, min: 0, max: score.Levels.Count - 1, unit: "level index", errors);
                    if (policy.ChoiceRules is not null)
                    {
                        errors.Add($"{id}: 'actions' is only valid for choice policies.");
                    }

                    break;

                case ChoiceQuestion choice:
                    if (policy.Thresholds is not null)
                    {
                        errors.Add($"{id}: 'thresholds' is only valid for noul and score policies.");
                    }

                    if (policy.ChoiceRules is null || policy.ChoiceRules.Count == 0)
                    {
                        errors.Add($"{id}: choice policies need at least one entry in 'actions'.");
                    }
                    else
                    {
                        foreach (var (option, rule) in policy.ChoiceRules)
                        {
                            if (!choice.Options.ContainsKey(option))
                            {
                                errors.Add($"{id}: action refers to unknown option '{option}'.");
                            }

                            if (rule.MinConfidence is < 0 or > 1)
                            {
                                errors.Add($"{id}: action '{option}' min_confidence must be between 0 and 1.");
                            }
                        }
                    }

                    break;

                default:
                    errors.Add($"{id}: unsupported question type {policy.Question.GetType().Name}.");
                    break;
            }
        }

        if (errors.Count > 0)
        {
            throw new PolicyValidationException(errors);
        }
    }

    private static void ValidateThresholds(string id, ActionThresholds? t, double min, double max, string unit, List<string> errors)
    {
        if (t is null || (t.Flag is null && t.Review is null && t.Block is null))
        {
            errors.Add($"{id}: 'thresholds' must set at least one of flag, review, block.");
            return;
        }

        foreach (var (name, value) in new[] { ("flag", t.Flag), ("review", t.Review), ("block", t.Block) })
        {
            if (value is { } v && (v < min || v > max))
            {
                errors.Add($"{id}: threshold '{name}' = {v} is outside the valid {unit} range [{min}, {max}].");
            }
        }

        var ordered = new[] { t.Flag, t.Review, t.Block }.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i] <= ordered[i - 1])
            {
                errors.Add($"{id}: thresholds must be strictly ascending (flag < review < block); got {string.Join(", ", ordered)}.");
                break;
            }
        }
    }
}

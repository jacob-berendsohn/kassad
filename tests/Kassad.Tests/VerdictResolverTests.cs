using Kassad.Engine;

namespace Kassad.Tests;

public class VerdictResolverTests
{
    [Theory]
    [InlineData(0.10, VerdictAction.Allow)]
    [InlineData(0.40, VerdictAction.Flag)]
    [InlineData(0.59, VerdictAction.Flag)]
    [InlineData(0.60, VerdictAction.Review)]
    [InlineData(0.85, VerdictAction.Block)]
    [InlineData(0.99, VerdictAction.Block)]
    public void Noul_thresholds_are_inclusive_lower_bounds(double p, VerdictAction expected)
    {
        var verdict = VerdictResolver.Resolve(TestPolicies.Injection(), new NoulAnswer(p));

        Assert.Equal(expected, verdict.Action);
        Assert.Equal(p, verdict.Value);
        Assert.Null(verdict.Confidence);
        Assert.False(verdict.FromError);
    }

    [Fact]
    public void Choice_rule_applies_when_confident()
    {
        var answer = new ChoiceAnswer("prohibited", new Dictionary<string, double> { ["support"] = 0.1, ["prohibited"] = 0.9 }, Confidence: 0.88);

        var verdict = VerdictResolver.Resolve(TestPolicies.RequestClass(), answer);

        Assert.Equal(VerdictAction.Block, verdict.Action);
        Assert.Equal(0.9, verdict.Value);
    }

    [Fact]
    public void Choice_rule_downgrades_to_review_when_under_rule_floor()
    {
        var answer = new ChoiceAnswer("prohibited", new Dictionary<string, double> { ["support"] = 0.45, ["prohibited"] = 0.55 }, Confidence: 0.2);

        var verdict = VerdictResolver.Resolve(TestPolicies.RequestClass(), answer);

        Assert.Equal(VerdictAction.Review, verdict.Action);
    }

    [Fact]
    public void Choice_without_rule_allows()
    {
        var answer = new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.95, ["prohibited"] = 0.05 }, Confidence: 0.93);

        Assert.Equal(VerdictAction.Allow, VerdictResolver.Resolve(TestPolicies.RequestClass(), answer).Action);
    }

    [Fact]
    public void Score_thresholds_apply_to_level_units()
    {
        var levels = new[] { "none", "minor", "serious", "severe" };
        var mild = new ScoreAnswer(1.4, levels, [0.1, 0.5, 0.3, 0.1], Confidence: 0.6);
        var bad = new ScoreAnswer(3.0, levels, [0.0, 0.0, 0.0, 1.0], Confidence: 0.99);

        Assert.Equal(VerdictAction.Allow, VerdictResolver.Resolve(TestPolicies.HarmSeverity(), mild).Action);
        Assert.Equal(VerdictAction.Block, VerdictResolver.Resolve(TestPolicies.HarmSeverity(), bad).Action);
    }

    [Fact]
    public void Error_honors_fail_closed_and_fail_open()
    {
        var boom = new DecisionModelException("down");

        var closed = VerdictResolver.FromError(TestPolicies.Injection(ErrorPolicy.FailClosed), boom);
        var open = VerdictResolver.FromError(TestPolicies.Injection(ErrorPolicy.FailOpen), boom);

        Assert.Equal(VerdictAction.Block, closed.Action);
        Assert.Equal(VerdictAction.Allow, open.Action);
        Assert.True(closed.FromError);
        Assert.Null(closed.Answer);
    }
}

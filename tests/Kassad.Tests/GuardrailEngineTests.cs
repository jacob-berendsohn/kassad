using Kassad.Engine;
using Kassad.Policies;

namespace Kassad.Tests;

public class GuardrailEngineTests
{
    private static PolicySet Set() => PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.RequestClass(), TestPolicies.HarmSeverity()]);

    [Fact]
    public async Task All_policies_for_a_stage_go_out_in_one_request()
    {
        var model = new FakeDecisionModel()
            .Answer("prompt_injection", new NoulAnswer(0.05))
            .Answer("request_class", new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.9, ["prohibited"] = 0.1 }, 0.9));
        var engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.Single(model.Requests);
        Assert.Equal(2, model.Requests[0].Questions.Count);
        Assert.Equal(VerdictAction.Allow, result.Outcome);
        Assert.Equal(2, result.Verdicts.Count);
        Assert.NotNull(result.Usage);
    }

    [Fact]
    public async Task Outcome_is_the_most_severe_verdict()
    {
        var model = new FakeDecisionModel()
            .Answer("prompt_injection", new NoulAnswer(0.5)) // Flag
            .Answer("request_class", new ChoiceAnswer("prohibited", new Dictionary<string, double> { ["support"] = 0.1, ["prohibited"] = 0.9 }, 0.95)); // Block
        var engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.Equal(VerdictAction.Block, result.Outcome);
    }

    [Fact]
    public async Task Model_failure_becomes_error_verdicts_per_policy()
    {
        var model = new FakeDecisionModel { ThrowOnEvaluate = new DecisionModelException("529") };
        var engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.True(result.HadModelError);
        Assert.Equal(VerdictAction.Block, result.Verdicts.Single(v => v.PolicyId == "prompt_injection").Action); // fail_closed
        Assert.Equal(VerdictAction.Allow, result.Verdicts.Single(v => v.PolicyId == "request_class").Action);   // fail_open
        Assert.Equal(VerdictAction.Block, result.Outcome);
    }

    [Fact]
    public async Task Missing_answer_is_treated_as_a_model_error_for_that_policy_only()
    {
        var model = new FakeDecisionModel().Answer("prompt_injection", new NoulAnswer(0.01)); // no answer for request_class
        var engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        var missing = result.Verdicts.Single(v => v.PolicyId == "request_class");
        Assert.True(missing.FromError);
        Assert.Equal(VerdictAction.Allow, missing.Action); // request_class is fail_open
        Assert.False(result.Verdicts.Single(v => v.PolicyId == "prompt_injection").FromError);
    }

    [Fact]
    public async Task Stage_with_no_policies_allows_without_calling_the_model()
    {
        var model = new FakeDecisionModel();
        var engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateAsync(Stage.Grounding, "anything");

        Assert.Empty(model.Requests);
        Assert.Equal(VerdictAction.Allow, result.Outcome);
        Assert.Equal(TimeSpan.Zero, result.ModelLatency);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_a_verdict()
    {
        var model = new FakeDecisionModel { ThrowOnEvaluate = new OperationCanceledException() };
        var engine = new GuardrailEngine(model, Set());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(Stage.Inbound, "x", cts.Token));
    }
}

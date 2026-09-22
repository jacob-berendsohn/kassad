using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.Options;

namespace Kassad.Tests;

public class GuardrailEngineTests
{
    private static PolicySet Set() => PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.RequestClass(), TestPolicies.HarmSeverity()]);

    private static GuardrailEngine WithBudget(FakeDecisionModel model, TimeSpan budget) =>
        new(model, Set(), Options.Create(new EvaluationOptions { Budget = budget }));

    private static FakeDecisionModel BenignAnswers(FakeDecisionModel model) => model
        .Answer("prompt_injection", new NoulAnswer(0.05))
        .Answer("request_class", new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.9, ["prohibited"] = 0.1 }, 0.9));

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

    // The 50 ms budget / one-minute model case with the suite's one wall-clock bound lives in
    // GuardrailEngineBudgetTimingTests; its comments say what that bound can and cannot show.

    [Fact]
    public async Task Model_that_answers_within_the_budget_is_unaffected()
    {
        var model = BenignAnswers(new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(10) });
        var engine = WithBudget(model, TimeSpan.FromSeconds(5));

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.False(result.BudgetExceeded);
        Assert.False(result.HadModelError);
        Assert.Equal(VerdictAction.Allow, result.Outcome);
        Assert.NotNull(result.Usage);
        Assert.False(model.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Without_a_budget_the_engine_waits_for_the_model()
    {
        var model = BenignAnswers(new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(150) });
        var engine = new GuardrailEngine(model, Set(), Options.Create(new EvaluationOptions()));

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.False(result.BudgetExceeded);
        Assert.Equal(VerdictAction.Allow, result.Outcome);
        Assert.False(model.LastToken.CanBeCanceled); // no linked source was created
    }

    [Fact]
    public async Task Caller_cancellation_propagates_even_when_a_budget_is_set()
    {
        // A delay the 20 ms cancellation cannot lose a race against on a contended CI runner (see GuardrailEngineTelemetryTests).
        var model = new FakeDecisionModel { Delay = TimeSpan.FromMinutes(1) };
        var engine = WithBudget(model, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(Stage.Inbound, "x", cts.Token));
        Assert.True(cts.IsCancellationRequested);
        Assert.True(model.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Already_cancelled_caller_propagates_before_the_budget_starts()
    {
        var model = new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(500) };
        var engine = WithBudget(model, TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(Stage.Inbound, "x", cts.Token));
    }

    [Fact]
    public async Task Model_failure_inside_the_budget_is_an_ordinary_model_error()
    {
        var model = new FakeDecisionModel { ThrowOnEvaluate = new DecisionModelException("529") };
        var engine = WithBudget(model, TimeSpan.FromSeconds(5));

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.False(result.BudgetExceeded);
        Assert.True(result.HadModelError);
        Assert.All(result.Verdicts, v => Assert.StartsWith("model error", v.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failure_after_the_budget_fired_is_attributed_to_the_budget_even_when_wrapped()
    {
        // A delay the 50 ms budget cannot lose a race against on a contended CI runner (see GuardrailEngineTelemetryTests).
        var model = new FakeDecisionModel { Delay = TimeSpan.FromMinutes(1), WrapCancellation = true };
        var engine = WithBudget(model, TimeSpan.FromMilliseconds(50));

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.True(result.BudgetExceeded);
        Assert.All(result.Verdicts, v => Assert.StartsWith("budget exceeded", v.Reason, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Budget_must_be_positive(int milliseconds)
    {
        var options = Options.Create(new EvaluationOptions { Budget = TimeSpan.FromMilliseconds(milliseconds) });

        Assert.Throws<ArgumentOutOfRangeException>(() => new GuardrailEngine(new FakeDecisionModel(), Set(), options));
    }
}

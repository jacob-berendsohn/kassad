using System.Diagnostics;
using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.Options;

namespace Kassad.Tests;

/// <summary>
/// Collection for the one test with a wall-clock bound. xunit runs a collection that opts out of parallelization
/// after every parallel collection has finished and on its own, so the bound measures the engine rather than the
/// runner: in the first CI run the same test, executed alongside five other classes under coverage instrumentation
/// on a 4-vCPU runner, measured 141 ms (windows, net8.0) and 545 ms (ubuntu, net10.0) against a 50 ms budget,
/// while eight local runs stayed well under 100 ms.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class BudgetTimingCollection
{
    public const string Name = "Budget timing";
}

[Collection(BudgetTimingCollection.Name)]
public class GuardrailEngineBudgetTimingTests
{
    private static PolicySet Set() => PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.RequestClass(), TestPolicies.HarmSeverity()]);

    private static GuardrailEngine WithBudget(FakeDecisionModel model, TimeSpan budget) =>
        new(model, Set(), Options.Create(new EvaluationOptions { Budget = budget }));

    private static FakeDecisionModel SlowBenignModel() => new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(500) }
        .Answer("prompt_injection", new NoulAnswer(0.05))
        .Answer("request_class", new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.9, ["prohibited"] = 0.1 }, 0.9));

    [Fact]
    public async Task Budget_overrun_becomes_error_verdicts_without_waiting_for_the_model()
    {
        // Warm-up: JIT the budget path and spin up the timer machinery before the measured call.
        await WithBudget(SlowBenignModel(), TimeSpan.FromMilliseconds(1)).EvaluateAsync(Stage.Inbound, "warm-up");

        var model = SlowBenignModel();
        var engine = WithBudget(model, TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");
        stopwatch.Stop();

        Assert.True(result.BudgetExceeded);
        Assert.True(result.HadModelError);
        Assert.Equal(VerdictAction.Block, result.Verdicts.Single(v => v.PolicyId == "prompt_injection").Action); // fail_closed
        Assert.Equal(VerdictAction.Allow, result.Verdicts.Single(v => v.PolicyId == "request_class").Action);   // fail_open
        Assert.Equal(VerdictAction.Block, result.Outcome);
        Assert.All(result.Verdicts, v =>
        {
            Assert.True(v.FromError);
            Assert.Null(v.Answer);
            Assert.StartsWith("budget exceeded", v.Reason, StringComparison.Ordinal);
        });
        Assert.Null(result.Usage);
        Assert.True(model.LastToken.IsCancellationRequested, "the model should have been cancelled through the linked token");
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }
}

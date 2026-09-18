using System.Diagnostics;
using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.Options;

namespace Kassad.Tests;

/// <summary>
/// Collection for the one test with a wall-clock bound. xunit runs a collection that opts out of parallelization on
/// its own after every parallel collection has finished, so no other test in this process competes with the measured
/// call; the trx files of six CI runs confirm it (none has another test of this process ending after this one
/// started). That is the only isolation a test can buy. <c>dotnet test</c> runs the solution's other test projects
/// and the other target framework concurrently on the same runner, and on a 4-vCPU CI machine another run's coverage
/// instrumentation, test-host start-up or coverage report has stalled this process for up to 945 ms: roadmap 1.2
/// measured 141 ms and 545 ms while this test was still in the parallel phase, and on 2026-09-18 it measured 945 ms
/// here, alone in this collection and after a warm-up, against a 50 ms budget and a fake that would have answered at
/// 500 ms. The bound below is therefore a magnitude check, not a latency measurement.
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

    // A model the budget cannot lose a race against: it answers after a minute, so a result with BudgetExceeded set
    // can only have come from the budget's cancellation, never from the model finishing first (the pattern of the 3.3
    // telemetry test and of the two 1.2 tests in GuardrailEngineTests).
    private static FakeDecisionModel SlowBenignModel() => new FakeDecisionModel { Delay = TimeSpan.FromMinutes(1) }
        .Answer("prompt_injection", new NoulAnswer(0.05))
        .Answer("request_class", new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.9, ["prohibited"] = 0.1 }, 0.9));

    [Fact]
    public async Task Budget_overrun_becomes_error_verdicts_without_waiting_for_the_model()
    {
        var model = SlowBenignModel();
        var engine = WithBudget(model, TimeSpan.FromMilliseconds(50));

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");
        stopwatch.Stop();

        // Had the engine waited for the model, the fake would have answered after its minute and this would be an
        // ordinary Allow with Usage set. BudgetExceeded, the null Usage and the cancelled token are what prove that
        // the call ended through the budget; none of them needs the clock.
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

        // The clock checks magnitude only: the budget is 50 ms, the model would take a minute, and the engine has to
        // come back within 5 s, a bound the worst stall CI has shown (945 ms) clears five times over. It cannot be
        // tighter than the runner: at 100 ms this assertion failed CI three times while every passing run finished
        // the whole test in 58-83 ms. A budget that fired seconds late still fails here; one that never fired fails
        // above, a minute later.
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }
}

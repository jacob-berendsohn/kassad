using Kassad.Engine;
using Kassad.Policies;

namespace Kassad.Tests;

public class GuardrailEngineExtensionsTests
{
    private static readonly ToolCallState ToolCall = new(
        "Show me my most recent invoice.",
        "delete_account",
        """{"type":"object","properties":{"account_id":{"type":"string"}},"required":["account_id"]}""",
        """{"account_id":"acct_42"}""");

    private static readonly GroundingState Grounding = new(
        "The treaty was signed in 1840.",
        "The Treaty of Waitangi was signed on 6 February 1840.",
        "doc-17");

    private static PolicySet Set() => PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.ToolCallOffIntent(), TestPolicies.ClaimUnsupported()]);

    [Fact]
    public async Task EvaluateToolCallAsync_runs_the_tool_call_policies_against_the_record()
    {
        var model = new FakeDecisionModel().Answer("tool_call_off_intent", new NoulAnswer(0.9));
        IGuardrailEngine engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateToolCallAsync(ToolCall);

        Assert.Equal(Stage.ToolCall, result.Stage);
        var request = Assert.Single(model.Requests);
        Assert.Same(ToolCall, request.State); // the record itself is the state; the decision model owns its wire shape
        Assert.Equal("tool_call_off_intent", Assert.Single(request.Questions.Keys)); // only this stage's policies went out
        var verdict = Assert.Single(result.Verdicts);
        Assert.Equal(0.9, Assert.IsType<NoulAnswer>(verdict.Answer).Probability);
        Assert.Equal(VerdictAction.Block, verdict.Action);
        Assert.Equal(VerdictAction.Block, result.Outcome);
    }

    [Fact]
    public async Task EvaluateGroundingAsync_runs_the_grounding_policies_against_the_record()
    {
        var model = new FakeDecisionModel().Answer("claim_unsupported", new NoulAnswer(0.1));
        IGuardrailEngine engine = new GuardrailEngine(model, Set());

        var result = await engine.EvaluateGroundingAsync(Grounding);

        Assert.Equal(Stage.Grounding, result.Stage);
        var request = Assert.Single(model.Requests);
        Assert.Same(Grounding, request.State);
        Assert.Equal("claim_unsupported", Assert.Single(request.Questions.Keys));
        var verdict = Assert.Single(result.Verdicts);
        Assert.Equal(0.1, Assert.IsType<NoulAnswer>(verdict.Answer).Probability);
        Assert.Equal(VerdictAction.Allow, result.Outcome);
    }

    [Fact]
    public async Task Helpers_hand_the_caller_token_to_the_model()
    {
        var model = new FakeDecisionModel().Answer("tool_call_off_intent", new NoulAnswer(0.1));
        IGuardrailEngine engine = new GuardrailEngine(model, Set()); // no budget, so the model sees the caller's token itself
        using var cts = new CancellationTokenSource();

        await engine.EvaluateToolCallAsync(ToolCall, cts.Token);

        Assert.Equal(cts.Token, model.LastToken);
    }

    [Fact]
    public async Task Stage_without_policies_allows_without_a_model_call()
    {
        var model = new FakeDecisionModel();
        IGuardrailEngine engine = new GuardrailEngine(model, PolicySet.FromPolicies([TestPolicies.Injection()]));

        var toolCall = await engine.EvaluateToolCallAsync(ToolCall);
        var grounding = await engine.EvaluateGroundingAsync(Grounding);

        Assert.Empty(model.Requests);
        Assert.Equal(VerdictAction.Allow, toolCall.Outcome);
        Assert.Equal(VerdictAction.Allow, grounding.Outcome);
        Assert.Equal(Stage.ToolCall, toolCall.Stage);
        Assert.Equal(Stage.Grounding, grounding.Stage);
    }

    [Fact]
    public async Task Helpers_reject_a_null_engine_or_state()
    {
        IGuardrailEngine engine = new GuardrailEngine(new FakeDecisionModel(), Set());

        await Assert.ThrowsAsync<ArgumentNullException>("engine", () => GuardrailEngineExtensions.EvaluateToolCallAsync(null!, ToolCall));
        await Assert.ThrowsAsync<ArgumentNullException>("toolCall", () => engine.EvaluateToolCallAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>("engine", () => GuardrailEngineExtensions.EvaluateGroundingAsync(null!, Grounding));
        await Assert.ThrowsAsync<ArgumentNullException>("grounding", () => engine.EvaluateGroundingAsync(null!));
    }

    [Fact]
    public void State_records_reject_null_required_fields()
    {
        Assert.Throws<ArgumentNullException>("UserIntent", () => new ToolCallState(null!, "tool", "{}", "{}"));
        Assert.Throws<ArgumentNullException>("ToolName", () => new ToolCallState("intent", null!, "{}", "{}"));
        Assert.Throws<ArgumentNullException>("ToolSchemaJson", () => new ToolCallState("intent", "tool", null!, "{}"));
        Assert.Throws<ArgumentNullException>("ArgumentsJson", () => new ToolCallState("intent", "tool", "{}", null!));
        Assert.Throws<ArgumentNullException>("Claim", () => new GroundingState(null!, "passage"));
        Assert.Throws<ArgumentNullException>("SourcePassage", () => new GroundingState("claim", null!));
        Assert.Null(new GroundingState("claim", "passage").SourceId); // the only optional field
    }
}

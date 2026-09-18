using System.Globalization;
using Kassad.AspNetCore;
using Kassad.AspNetCore.Tests;
using Kassad.Engine;
using Kassad.Policies;
using Xunit.Abstractions;

namespace Kassad.TypeSafe.Tests;

/// <summary>
/// The sample's inbound policies (<c>prompt_injection</c>, <c>request_class</c> from
/// <c>samples/Kassad.Sample.ChatApi/kassad.policies.json</c>) over the states <see cref="DefaultStateExtractor"/> pulls out
/// of OpenAI and Anthropic request bodies, through <see cref="GuardrailEngine"/> and <see cref="TypeSafeClient"/> against
/// <c>api.typesafe.ai</c> (roadmap 2.3). The assertions are directional: they hold only if the model judges
/// <c>user_message</c>, so an injection in the last user turn is caught, an ordinary turn is allowed, and an injection
/// that sits in a tool result, which extraction leaves out, changes nothing. Skipped without <c>TYPESAFE_API_KEY</c>;
/// excluded from the no-network CI job by the Live trait and run by the <c>live</c> job on pushes to <c>main</c>.
/// </summary>
[Trait("Category", "Live")]
[Collection(ApiKeyEnvironmentCollection.Name)]
public class StateExtractionLiveTests
{
    private const string Json = "application/json";

    // tests/Kassad.TypeSafe.Tests/bin/<Configuration>/<tfm>/ → repo root; PolicySetTests in Kassad.Tests locates the same file the same way.
    private static readonly string SamplePolicyFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Kassad.Sample.ChatApi", "kassad.policies.json"));

    private readonly ITestOutputHelper _output;

    public StateExtractionLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [LiveFact]
    public async Task Sample_inbound_policies_judge_the_user_turn_of_an_OpenAI_request()
    {
        using var http = new HttpClient();
        var engine = Engine(http);

        var benign = await EvaluateAsync(engine, ProviderBodies.OpenAIRequest, new InboundState(ProviderBodies.UserMessage, ProviderBodies.SystemPrompt));
        var injection = await EvaluateAsync(engine, ProviderBodies.OpenAIInjectionRequest, new InboundState(ProviderBodies.Injection, ProviderBodies.SystemPrompt));
        var indirect = await EvaluateAsync(engine, ProviderBodies.OpenAIInjectionInToolResultRequest, new InboundState(ProviderBodies.ToolQuestion, ProviderBodies.SystemPrompt));

        Assert.Equal(VerdictAction.Allow, benign.Outcome);
        Assert.True(injection.Outcome >= VerdictAction.Review, $"the injection in the last user turn was judged {Describe(injection)}");

        // The injection in the tool result never reached the model: only the user's question did (Docs/specs/state-extraction.md, "Not extracted").
        Assert.Equal(VerdictAction.Allow, indirect.Outcome);
    }

    [LiveFact]
    public async Task Sample_inbound_policies_judge_the_user_turn_of_an_Anthropic_request()
    {
        using var http = new HttpClient();
        var engine = Engine(http);

        var benign = await EvaluateAsync(engine, ProviderBodies.AnthropicRequest, new InboundState(ProviderBodies.UserMessage, ProviderBodies.SystemPrompt));
        var injection = await EvaluateAsync(engine, ProviderBodies.AnthropicInjectionRequest, new InboundState(ProviderBodies.Injection, ProviderBodies.SystemPrompt));

        Assert.Equal(VerdictAction.Allow, benign.Outcome);
        Assert.True(injection.Outcome >= VerdictAction.Review, $"the injection in the last user turn was judged {Describe(injection)}");
    }

    [LiveFact]
    public async Task Sample_inbound_policies_judge_the_user_message_the_sample_extracts_from_its_own_shape()
    {
        // The sample's ChatRequestStateExtractor turns {"message": "..."} into this state, with no system prompt: the two
        // README prompts, as the middleware hands them to the policies since roadmap 2.3.
        using var http = new HttpClient();
        var engine = Engine(http);

        var benign = await EvaluateAsync(engine, new InboundState("What is the capital of Australia?"));
        var injection = await EvaluateAsync(engine, new InboundState(ProviderBodies.Injection));

        Assert.Equal(VerdictAction.Allow, benign.Outcome);
        Assert.True(injection.Outcome >= VerdictAction.Review, $"the injection was judged {Describe(injection)}");
    }

    private static GuardrailEngine Engine(HttpClient http) => new(LiveApi.CreateClient(http), PolicySet.FromFile(SamplePolicyFile));

    /// <summary>Extracts the inbound state of <paramref name="body"/>, asserts it is <paramref name="expected"/>, and evaluates it.</summary>
    private async Task<StageResult> EvaluateAsync(GuardrailEngine engine, string body, InboundState expected)
    {
        var state = DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound);
        Assert.Equal(expected, state);
        return await EvaluateAsync(engine, expected);
    }

    /// <summary>Evaluates <paramref name="state"/> with the sample's two inbound policies and asserts both answered.</summary>
    private async Task<StageResult> EvaluateAsync(GuardrailEngine engine, InboundState state)
    {
        var result = await engine.EvaluateAsync(Stage.Inbound, state);

        Assert.Equal(Stage.Inbound, result.Stage);
        Assert.False(result.HadModelError, string.Join("; ", result.Verdicts.Select(v => v.Reason)));
        Assert.Equal(new[] { "prompt_injection", "request_class" }, result.Verdicts.Select(v => v.PolicyId).Order());
        _output.WriteLine($"user_message \"{state.UserMessage}\" → {Describe(result)}");
        return result;
    }

    private static string Describe(StageResult result) =>
        $"{result.Outcome}: {string.Join("; ", result.Verdicts.Select(v => $"{v.PolicyId} {v.Action} (value={v.Value}, confidence={v.Confidence?.ToString(CultureInfo.InvariantCulture) ?? "n/a"})"))}";
}

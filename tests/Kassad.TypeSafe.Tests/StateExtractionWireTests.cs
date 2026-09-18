using System.Text.Json;
using Kassad.AspNetCore;
using Kassad.AspNetCore.Tests;

namespace Kassad.TypeSafe.Tests;

/// <summary>
/// The states <c>Kassad.AspNetCore</c> hands the engine, as <see cref="SystemOneWire"/> writes them. <c>Kassad.TypeSafe</c> does
/// not reference <c>Kassad.AspNetCore</c>, so <see cref="InboundState"/> and <see cref="KassadDelegatingHandler.OutboundState"/>
/// take the default path (System.Text.Json with web defaults) and their own attributes have to produce the field names
/// <c>Docs/specs/state-extraction.md</c> documents.
/// </summary>
public class StateExtractionWireTests
{
    private static IReadOnlyDictionary<string, Question> Questions => FixtureRequests.AllTypes.Questions;

    [Fact]
    public void Extracted_inbound_state_is_written_with_the_documented_field_names()
    {
        var state = DefaultStateExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, "application/json", Stage.Inbound);
        var wire = WriteState(Assert.IsType<InboundState>(state));

        Assert.Equal(new[] { "user_message", "system_prompt" }, wire.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ProviderBodies.UserMessage, wire.GetProperty("user_message").GetString());
        Assert.Equal(ProviderBodies.SystemPrompt, wire.GetProperty("system_prompt").GetString());
    }

    [Fact]
    public void Inbound_state_without_a_system_prompt_leaves_the_field_out()
    {
        var wire = WriteState(new InboundState("What is the capital of Australia?"));

        Assert.Equal(new[] { "user_message" }, wire.EnumerateObject().Select(p => p.Name));
        Assert.Equal("What is the capital of Australia?", wire.GetProperty("user_message").GetString());
    }

    [Fact]
    public void Outbound_state_is_written_with_each_body_once()
    {
        var extracted = new KassadDelegatingHandler.OutboundState(null, null) { UserMessage = "hi", SystemPrompt = "Be terse.", Completion = "Hello." };
        var raw = new KassadDelegatingHandler.OutboundState("""{"q":"hi"}""", """{"a":"Hello."}""");
        var bodiless = new KassadDelegatingHandler.OutboundState(null, null) { Completion = "Hello." };

        Assert.Equal(new[] { "user_message", "system_prompt", "completion" }, WriteState(extracted).EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "request", "response" }, WriteState(raw).EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "completion" }, WriteState(bodiless).EnumerateObject().Select(p => p.Name));
        Assert.Equal("""{"q":"hi"}""", WriteState(raw).GetProperty("request").GetString()); // the raw body travels as a string, not as nested JSON
    }

    /// <summary>The <c>state</c> element of the request <see cref="SystemOneWire"/> writes for <paramref name="state"/>.</summary>
    private static JsonElement WriteState(object state)
    {
        var bytes = SystemOneWire.WriteRequest(new DecisionRequest(state, Questions), "jev-latest");
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.GetProperty("state").Clone();
    }
}

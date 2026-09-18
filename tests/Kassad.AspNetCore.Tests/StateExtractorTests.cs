using System.Text.Json;

namespace Kassad.AspNetCore.Tests;

/// <summary>
/// The built-in <see cref="IStateExtractor"/>s on their own, over the provider bodies in <see cref="ProviderBodies"/> and
/// the edge shapes <c>Docs/specs/state-extraction.md</c> spells out: what is extracted, what falls back to the whole body,
/// and how the states serialize for a decision model.
/// </summary>
public class StateExtractorTests
{
    private const string Json = "application/json";
    private static readonly InboundState Benign = new(ProviderBodies.UserMessage, ProviderBodies.SystemPrompt);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(ProviderBodies.OpenAIRequest)]
    [InlineData(ProviderBodies.AnthropicRequest)]
    public void Chat_request_is_reduced_to_the_last_user_turn_and_the_system_prompt(string body)
    {
        var state = DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound);

        // Two user turns, an assistant turn, an image part: only the text of the last user turn and the system prompt survive.
        Assert.Equal(Benign, state);
    }

    [Theory]
    [InlineData(ProviderBodies.OpenAIResponse)]
    [InlineData(ProviderBodies.AnthropicResponse)]
    public void Chat_response_is_reduced_to_the_reply(string body)
    {
        Assert.Equal(ProviderBodies.Reply, DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Theory]
    [InlineData(ProviderBodies.OpenAIInjectionRequest)]
    [InlineData(ProviderBodies.AnthropicInjectionRequest)]
    public void Injection_in_the_last_user_turn_is_the_user_message(string body)
    {
        Assert.Equal(new InboundState(ProviderBodies.Injection, ProviderBodies.SystemPrompt), DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Fact]
    public void Tool_messages_are_not_user_turns()
    {
        // The injection sits in a tool result after the user's question; the user message is the question. The spec
        // records the consequence: content that reaches the model through tool results is not what inbound policies see.
        var state = DefaultStateExtractor.Instance.Extract(ProviderBodies.OpenAIInjectionInToolResultRequest, Json, Stage.Inbound);

        Assert.Equal(new InboundState(ProviderBodies.ToolQuestion, ProviderBodies.SystemPrompt), state);
    }

    [Fact]
    public void Provider_extractors_read_the_system_prompt_where_their_provider_puts_it()
    {
        Assert.Equal(Benign, OpenAIChatCompletionsExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, Stage.Inbound));
        Assert.Equal(Benign, AnthropicMessagesExtractor.Instance.Extract(ProviderBodies.AnthropicRequest, Json, Stage.Inbound));

        // Each reads its own shape faithfully: the other provider's system prompt location is ignored, the user turn is not.
        Assert.Equal(new InboundState(ProviderBodies.UserMessage), OpenAIChatCompletionsExtractor.Instance.Extract(ProviderBodies.AnthropicRequest, Json, Stage.Inbound));
        Assert.Equal(new InboundState(ProviderBodies.UserMessage), AnthropicMessagesExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, Stage.Inbound));
    }

    [Fact]
    public void Provider_extractors_read_only_their_own_response_shape()
    {
        Assert.Equal(ProviderBodies.Reply, OpenAIChatCompletionsExtractor.Instance.Extract(ProviderBodies.OpenAIResponse, Json, Stage.Outbound));
        Assert.Equal(ProviderBodies.Reply, AnthropicMessagesExtractor.Instance.Extract(ProviderBodies.AnthropicResponse, Json, Stage.Outbound));

        Assert.Null(OpenAIChatCompletionsExtractor.Instance.Extract(ProviderBodies.AnthropicResponse, Json, Stage.Outbound));
        Assert.Null(AnthropicMessagesExtractor.Instance.Extract(ProviderBodies.OpenAIResponse, Json, Stage.Outbound));
    }

    [Fact]
    public void Developer_messages_are_system_prompt()
    {
        const string body = """{"model":"o3","messages":[{"role":"developer","content":"Be terse."},{"role":"user","content":"hi"}]}""";

        Assert.Equal(new InboundState("hi", "Be terse."), DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Fact]
    public void Several_system_messages_are_joined_with_blank_lines()
    {
        const string body = """{"messages":[{"role":"system","content":"Rule one."},{"role":"user","content":"hi"},{"role":"system","content":"Rule two."},{"role":"user","content":"again"}]}""";

        Assert.Equal(new InboundState("again", "Rule one.\n\nRule two."), DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Fact]
    public void Anthropic_system_blocks_are_joined_with_newlines()
    {
        const string body = """{"system":[{"type":"text","text":"Rule one.","cache_control":{"type":"ephemeral"}},{"type":"text","text":"Rule two."}],"messages":[{"role":"user","content":"hi"}]}""";

        Assert.Equal(new InboundState("hi", "Rule one.\nRule two."), DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Fact]
    public void Entries_that_are_not_message_objects_are_skipped()
    {
        const string body = """{"messages":["hi",3,null,{"content":"no role"},{"role":7,"content":"numeric role"},{"role":"user","content":"ok"}]}""";

        Assert.Equal(new InboundState("ok"), DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Theory]
    [InlineData("""{"messages":[{"role":"system","content":"x"},{"role":"assistant","content":"y"}]}""")] // no user turn
    [InlineData("""{"messages":[{"role":"user","content":""}]}""")] // empty text
    [InlineData("""{"messages":[{"role":"user","content":[]}]}""")] // no parts
    [InlineData("""{"messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"https://example.test/a.png"}}]}]}""")] // image only
    [InlineData("""{"messages":[{"role":"user","content":{"text":"an object, not a string or parts"}}]}""")]
    [InlineData("""{"messages":[{"role":"user"}]}""")] // no content
    [InlineData("""{"messages":[{"role":"user","content":"earlier"},{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"result text"}]}]}""")] // Anthropic tool result only
    public void Chat_request_without_a_readable_user_turn_is_evaluated_whole(string body)
    {
        Assert.Equal(body, DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
        Assert.Null(OpenAIChatCompletionsExtractor.Instance.Extract(body, Json, Stage.Inbound));
        Assert.Null(AnthropicMessagesExtractor.Instance.Extract(body, Json, Stage.Inbound));
    }

    [Fact]
    public void Several_choices_are_joined_with_blank_lines()
    {
        const string body = """{"choices":[{"index":0,"message":{"role":"assistant","content":"First."}},{"index":1,"message":{"role":"assistant","content":"Second."}}]}""";

        Assert.Equal("First.\n\nSecond.", DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Fact]
    public void Refusal_is_the_reply_when_the_content_is_empty()
    {
        const string body = """{"choices":[{"message":{"role":"assistant","content":null,"refusal":"I can't help with that."}}]}""";

        Assert.Equal("I can't help with that.", DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Fact]
    public void Anthropic_text_blocks_are_joined_with_newlines()
    {
        const string body = """{"type":"message","role":"assistant","content":[{"type":"text","text":"First."},{"type":"tool_use","id":"toolu_1","name":"search","input":{}},{"type":"text","text":"Second."}]}""";

        Assert.Equal("First.\nSecond.", DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Theory]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"search","arguments":"{}"}}]}}]}""")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"text":"legacy completions shape"}]}""")]
    [InlineData("""{"type":"message","role":"assistant","content":[{"type":"tool_use","id":"toolu_1","name":"search","input":{}}]}""")]
    [InlineData("""{"type":"message","role":"assistant","content":"a string, not blocks"}""")]
    [InlineData("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""")]
    public void Chat_response_without_a_readable_reply_is_evaluated_whole(string body)
    {
        Assert.Null(DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("application/vnd.api+json", true)]
    [InlineData("text/json", true)]
    [InlineData(null, true)]
    [InlineData("text/plain", false)]
    [InlineData("text/plain; charset=utf-8", false)]
    [InlineData("application/x-www-form-urlencoded", false)]
    public void Content_type_decides_whether_a_body_is_parsed(string? contentType, bool parsed)
    {
        var inbound = DefaultStateExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, contentType, Stage.Inbound);
        var outbound = DefaultStateExtractor.Instance.Extract(ProviderBodies.OpenAIResponse, contentType, Stage.Outbound);

        if (parsed)
        {
            Assert.Equal(Benign, inbound);
            Assert.Equal(ProviderBodies.Reply, outbound);
        }
        else
        {
            Assert.Equal(ProviderBodies.OpenAIRequest, inbound);
            Assert.Null(outbound);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("""{"messages":"not an array"}""")]
    [InlineData("""{"prompt":"a legacy completions request"}""")]
    [InlineData(ProviderBodies.UnknownJson)]
    public void Body_without_a_recognised_shape_is_evaluated_whole(string body)
    {
        Assert.Equal(body, DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
        Assert.Null(DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Fact]
    public void Json_nested_past_the_reader_limit_is_evaluated_whole_without_throwing()
    {
        var body = new string('[', 200) + new string(']', 200);

        Assert.Equal(body, DefaultStateExtractor.Instance.Extract(body, Json, Stage.Inbound));
        Assert.Null(DefaultStateExtractor.Instance.Extract(body, Json, Stage.Outbound));
    }

    [Theory]
    [InlineData(Stage.ToolCall)]
    [InlineData(Stage.Grounding)]
    public void Stages_without_an_http_body_yield_nothing(Stage stage)
    {
        Assert.Null(DefaultStateExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, stage));
        Assert.Null(OpenAIChatCompletionsExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, stage));
        Assert.Null(AnthropicMessagesExtractor.Instance.Extract(ProviderBodies.AnthropicRequest, Json, stage));
        Assert.Null(RawBodyExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, stage));
    }

    [Fact]
    public void Raw_body_extractor_returns_the_body_for_a_request_and_nothing_for_a_response()
    {
        Assert.Same(ProviderBodies.OpenAIRequest, RawBodyExtractor.Instance.Extract(ProviderBodies.OpenAIRequest, Json, Stage.Inbound));
        Assert.Null(RawBodyExtractor.Instance.Extract(ProviderBodies.OpenAIResponse, Json, Stage.Outbound));
    }

    [Fact]
    public void Extractors_and_InboundState_guard_their_arguments()
    {
        Assert.Throws<ArgumentNullException>("body", () => DefaultStateExtractor.Instance.Extract(null!, Json, Stage.Inbound));
        Assert.Throws<ArgumentNullException>("body", () => OpenAIChatCompletionsExtractor.Instance.Extract(null!, Json, Stage.Inbound));
        Assert.Throws<ArgumentNullException>("body", () => AnthropicMessagesExtractor.Instance.Extract(null!, Json, Stage.Inbound));
        Assert.Throws<ArgumentNullException>("body", () => RawBodyExtractor.Instance.Extract(null!, Json, Stage.Inbound));
        Assert.Throws<ArgumentNullException>("UserMessage", () => new InboundState(null!));
    }

    [Fact]
    public void InboundState_serializes_under_the_documented_names_and_omits_a_missing_system_prompt()
    {
        // JsonSerializerDefaults.Web is what Kassad.TypeSafe serializes an unknown state with; the names must not depend on its naming policy.
        Assert.Equal("""{"user_message":"hi","system_prompt":"Be terse."}""", JsonSerializer.Serialize(new InboundState("hi", "Be terse."), Web));
        Assert.Equal("""{"user_message":"hi"}""", JsonSerializer.Serialize(new InboundState("hi"), Web));
    }

    [Fact]
    public void OutboundState_serializes_each_body_once_under_the_documented_names()
    {
        var extracted = new KassadDelegatingHandler.OutboundState(null, null) { UserMessage = "hi", SystemPrompt = "Be terse.", Completion = "Hello." };
        var raw = new KassadDelegatingHandler.OutboundState("""{"q":"hi"}""", """{"a":"Hello."}""");
        var mixed = new KassadDelegatingHandler.OutboundState(null, """{"a":"Hello."}""") { UserMessage = "hi" };
        var bodiless = new KassadDelegatingHandler.OutboundState(null, null) { Completion = "Hello." };

        Assert.Equal("""{"user_message":"hi","system_prompt":"Be terse.","completion":"Hello."}""", JsonSerializer.Serialize(extracted, Web));
        Assert.Equal("""{"completion":"Hello."}""", JsonSerializer.Serialize(bodiless, Web));

        // A whole body travels as a string value; the default encoder writes its quotes as unicode escapes, so compare parsed.
        var rawWire = Parse(raw);
        Assert.Equal(new[] { "request", "response" }, rawWire.Select(p => p.Name));
        Assert.Equal("""{"q":"hi"}""", rawWire.Single(p => p.Name == "request").Value.GetString());
        Assert.Equal("""{"a":"Hello."}""", rawWire.Single(p => p.Name == "response").Value.GetString());

        var mixedWire = Parse(mixed);
        Assert.Equal(new[] { "response", "user_message" }, mixedWire.Select(p => p.Name));
        Assert.Equal("""{"a":"Hello."}""", mixedWire.Single(p => p.Name == "response").Value.GetString());
        Assert.Equal("hi", mixedWire.Single(p => p.Name == "user_message").Value.GetString());
    }

    private static List<JsonProperty> Parse(KassadDelegatingHandler.OutboundState state)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state, Web));
        return document.RootElement.EnumerateObject().Select(p => new JsonProperty(p.Name, p.Value.Clone())).ToList();
    }

    private sealed record JsonProperty(string Name, JsonElement Value);
}

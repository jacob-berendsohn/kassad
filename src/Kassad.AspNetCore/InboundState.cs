using System.Text.Json.Serialization;

namespace Kassad.AspNetCore;

/// <summary>
/// State for the <see cref="Stage.Inbound"/> stage when the body is a recognised chat request: the user's latest message
/// and the system prompt, without the JSON envelope around them. Produced by the built-in extractors for OpenAI
/// chat-completions and Anthropic messages bodies; return one from your own <see cref="IStateExtractor"/> so policies
/// written against these fields work for your shape too.
/// </summary>
/// <remarks>
/// A decision model that serializes states with <c>System.Text.Json</c>, as <c>Kassad.TypeSafe</c> does, presents this
/// state as an object with the fields <c>user_message</c> and, when there is one, <c>system_prompt</c>; policy
/// instructions can refer to them by name. Earlier turns, assistant messages, tool calls and tool results are not part
/// of it: <c>Docs/specs/state-extraction.md</c> lists what is and is not extracted.
/// </remarks>
/// <param name="UserMessage">The text of the last message with role <c>user</c>: a string content as-is, or the <c>text</c> parts of an array content joined with newlines.</param>
/// <param name="SystemPrompt">The system prompt: Anthropic's top-level <c>system</c>, or OpenAI's <c>system</c> and <c>developer</c> messages joined with blank lines. <c>null</c> when the request has none.</param>
public sealed record InboundState(string UserMessage, string? SystemPrompt = null)
{
    /// <summary>The text of the user's latest message.</summary>
    [JsonPropertyName("user_message")]
    public string UserMessage { get; init; } = UserMessage ?? throw new ArgumentNullException(nameof(UserMessage));

    /// <summary>The system prompt, or <c>null</c> when the request has none. Left out of the serialized state when <c>null</c>.</summary>
    [JsonPropertyName("system_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemPrompt { get; init; } = SystemPrompt;
}

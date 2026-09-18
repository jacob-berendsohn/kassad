using System.Text.Json;

namespace Kassad.AspNetCore;

/// <summary>
/// The <see cref="KassadOptions.StateExtractor"/> default: selects a reader by content type and shape. A body whose
/// content type names JSON (or that arrives without one) is parsed; a request with a <c>messages</c> array goes to
/// <see cref="AnthropicMessagesExtractor"/> when it has a top-level <c>system</c> field and to
/// <see cref="OpenAIChatCompletionsExtractor"/> otherwise; a response is read as an OpenAI chat completion
/// (<c>choices[]</c>) or an Anthropic message (<c>"type": "message"</c>), whichever it is. Everything else, including
/// <c>text/plain</c> bodies, invalid JSON and chat bodies without a user turn, is the <see cref="RawBodyExtractor"/>'s:
/// the whole request as the inbound state, the whole response as the outbound state's <c>response</c>.
/// </summary>
/// <remarks>
/// The two request shapes differ only in where the system prompt lives, so the one rule (top-level <c>system</c> means
/// Anthropic) cannot lose a user message; a body without a system prompt reads the same either way. To recognise a
/// shape of your own, implement <see cref="IStateExtractor"/> and hand every other body to <see cref="Instance"/>.
/// </remarks>
public sealed class DefaultStateExtractor : IStateExtractor
{
    private DefaultStateExtractor()
    {
    }

    /// <summary>The shared instance; the extractor holds no state.</summary>
    public static DefaultStateExtractor Instance { get; } = new();

    /// <inheritdoc />
    public object? Extract(string body, string? contentType, Stage stage)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (stage is Stage.Inbound or Stage.Outbound)
        {
            using var document = ChatJson.TryParse(body, contentType);
            if (document is not null)
            {
                var root = document.RootElement;
                object? extracted = stage == Stage.Inbound
                    ? (IsAnthropicRequest(root) ? AnthropicMessagesExtractor.ReadRequest(root) : OpenAIChatCompletionsExtractor.ReadRequest(root))
                    : OpenAIChatCompletionsExtractor.ReadResponse(root) ?? AnthropicMessagesExtractor.ReadResponse(root);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }

        return RawBodyExtractor.Instance.Extract(body, contentType, stage);
    }

    /// <summary>Anthropic puts the system prompt in a top-level <c>system</c> field; OpenAI has no such field.</summary>
    private static bool IsAnthropicRequest(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("system", out _);
}

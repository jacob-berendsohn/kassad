using System.Text.Json;

namespace Kassad.AspNetCore;

/// <summary>
/// Reads Anthropic Messages API bodies (<c>POST /v1/messages</c>). A request becomes an <see cref="InboundState"/>:
/// <c>user_message</c> is the content of the last <c>messages[]</c> entry with role <c>user</c>, <c>system_prompt</c> the
/// top-level <c>system</c> value, a string or an array of text blocks. A response (<c>"type": "message"</c>) becomes
/// the assistant's reply: the <c>text</c> of its <c>content[]</c> text blocks, joined with newlines.
/// </summary>
/// <remarks>
/// A <c>content</c> that is an array of blocks contributes its <c>text</c> blocks, joined with newlines; image and
/// document blocks, <c>tool_use</c> and <c>tool_result</c> blocks are dropped, so a user message that carries only
/// tool results has no text and the request reads as <c>null</c>. Bodies that are not JSON or not this shape read as
/// <c>null</c> too. <see cref="DefaultStateExtractor"/> selects this reader by shape; use it directly when every body
/// is known to be Anthropic-shaped.
/// </remarks>
public sealed class AnthropicMessagesExtractor : IStateExtractor
{
    private AnthropicMessagesExtractor()
    {
    }

    /// <summary>The shared instance; the extractor holds no state.</summary>
    public static AnthropicMessagesExtractor Instance { get; } = new();

    /// <inheritdoc />
    public object? Extract(string body, string? contentType, Stage stage)
    {
        ArgumentNullException.ThrowIfNull(body);

        using var document = ChatJson.TryParse(body, contentType);
        if (document is null)
        {
            return null;
        }

        return stage switch
        {
            Stage.Inbound => ReadRequest(document.RootElement),
            Stage.Outbound => ReadResponse(document.RootElement),
            _ => null,
        };
    }

    /// <summary>The request shape: <c>messages[]</c> with the top-level <c>system</c> as the system prompt.</summary>
    internal static InboundState? ReadRequest(JsonElement root) => ChatJson.ReadRequest(root, topLevelSystem: true, []);

    /// <summary>The response shape: an object of <c>type</c> <c>message</c> whose <c>content</c> is an array of blocks.</summary>
    internal static string? ReadResponse(JsonElement root)
    {
        if (!string.Equals(ChatJson.StringProperty(root, "type"), "message", StringComparison.Ordinal)
            || !root.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return ChatJson.Text(content);
    }
}

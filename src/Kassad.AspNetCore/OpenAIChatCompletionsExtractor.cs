using System.Text.Json;

namespace Kassad.AspNetCore;

/// <summary>
/// Reads OpenAI chat-completions bodies (<c>POST /v1/chat/completions</c> and the many APIs compatible with it).
/// A request becomes an <see cref="InboundState"/>: <c>user_message</c> is the content of the last <c>messages[]</c>
/// entry with role <c>user</c>, <c>system_prompt</c> the content of the <c>system</c> and <c>developer</c> entries joined
/// with blank lines. A response becomes the assistant's reply: <c>choices[].message.content</c> (or <c>refusal</c> when
/// the content is empty), the choices joined with blank lines when there are several.
/// </summary>
/// <remarks>
/// A <c>content</c> that is an array of parts contributes its <c>text</c> parts, joined with newlines; image, audio and
/// file parts are dropped. Tool calls and <c>tool</c> messages are not part of either state. Bodies that are not JSON
/// or not this shape, and responses whose choices carry no text (a tool call with no content), read as <c>null</c>.
/// <see cref="DefaultStateExtractor"/> selects this reader by shape; use it directly when every body is known to be OpenAI-shaped.
/// </remarks>
public sealed class OpenAIChatCompletionsExtractor : IStateExtractor
{
    private static readonly string[] SystemRoles = ["system", "developer"];

    private OpenAIChatCompletionsExtractor()
    {
    }

    /// <summary>The shared instance; the extractor holds no state.</summary>
    public static OpenAIChatCompletionsExtractor Instance { get; } = new();

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

    /// <summary>The request shape: <c>messages[]</c> with <c>system</c> and <c>developer</c> roles as the system prompt.</summary>
    internal static InboundState? ReadRequest(JsonElement root) => ChatJson.ReadRequest(root, topLevelSystem: false, SystemRoles);

    /// <summary>The response shape: <c>choices[].message.content</c>, falling back to <c>refusal</c> per choice.</summary>
    internal static string? ReadResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string>? texts = null;
        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object
                || !choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = message.TryGetProperty("content", out var content) ? ChatJson.Text(content) : null;
            text ??= message.TryGetProperty("refusal", out var refusal) ? ChatJson.Text(refusal) : null;
            if (text is not null)
            {
                (texts ??= []).Add(text);
            }
        }

        return texts is null ? null : string.Join("\n\n", texts);
    }
}

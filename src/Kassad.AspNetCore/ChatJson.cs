using System.Text.Json;

namespace Kassad.AspNetCore;

/// <summary>
/// The JSON reading the built-in extractors share: the content-type gate, the walk over a chat request's
/// <c>messages</c> array, and the text of a <c>content</c> value in either provider's shape. Nothing here throws for
/// any input; a body that is not JSON, or not the expected shape, reads as <c>null</c>.
/// </summary>
internal static class ChatJson
{
    /// <summary>
    /// Parses <paramref name="body"/> when <paramref name="contentType"/> names JSON (any media type containing
    /// <c>json</c>, so <c>application/json</c> and <c>application/vnd.api+json</c> both qualify) or is absent. A
    /// <c>text/plain</c> or other non-JSON body is never parsed, whatever it contains.
    /// </summary>
    /// <returns>The document, which the caller disposes, or <c>null</c> when the body is not parsed or is not valid JSON (malformed, or nested deeper than the default limit).</returns>
    public static JsonDocument? TryParse(string body, string? contentType)
    {
        if (contentType is not null && !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The user's latest message and the system prompt of a chat request: a JSON object with a <c>messages</c> array of
    /// <c>{ role, content }</c> objects. The system prompt is gathered from the top-level <c>system</c> value when
    /// <paramref name="topLevelSystem"/> is set (Anthropic) and from every message whose role is in
    /// <paramref name="systemRoles"/> (OpenAI's <c>system</c> and <c>developer</c>), in that order, joined with blank lines.
    /// </summary>
    /// <returns><c>null</c> when the root is not an object with a <c>messages</c> array, when no message has role <c>user</c>, or when the last one carries no text.</returns>
    public static InboundState? ReadRequest(JsonElement root, bool topLevelSystem, ReadOnlySpan<string> systemRoles)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string>? system = null;
        if (topLevelSystem && root.TryGetProperty("system", out var top) && Text(top) is { } topText)
        {
            (system ??= []).Add(topText);
        }

        JsonElement? lastUser = null;
        foreach (var message in messages.EnumerateArray())
        {
            var role = StringProperty(message, "role");
            if (role is null)
            {
                continue;
            }

            if (string.Equals(role, "user", StringComparison.Ordinal))
            {
                lastUser = message;
            }
            else if (systemRoles.Contains(role) && message.TryGetProperty("content", out var content) && Text(content) is { } systemText)
            {
                (system ??= []).Add(systemText);
            }
        }

        if (lastUser is not { } user
            || !user.TryGetProperty("content", out var userContent)
            || Text(userContent) is not { } userText)
        {
            return null;
        }

        return new InboundState(userText, system is null ? null : string.Join("\n\n", system));
    }

    /// <summary>
    /// The text of a <c>content</c> value: a string as it is, or, for an array of parts (OpenAI) or blocks (Anthropic),
    /// the <c>text</c> of every entry whose <c>type</c> is <c>text</c>, joined with newlines. Images, audio, documents,
    /// tool calls and tool results contribute nothing.
    /// </summary>
    /// <returns><c>null</c> when the value is neither a string nor an array, or holds no text.</returns>
    public static string? Text(JsonElement content)
    {
        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                var text = content.GetString();
                return string.IsNullOrEmpty(text) ? null : text;

            case JsonValueKind.Array:
                List<string>? parts = null;
                foreach (var part in content.EnumerateArray())
                {
                    if (string.Equals(StringProperty(part, "type"), "text", StringComparison.Ordinal)
                        && StringProperty(part, "text") is { Length: > 0 } partText)
                    {
                        (parts ??= []).Add(partText);
                    }
                }

                return parts is null ? null : string.Join("\n", parts);

            default:
                return null;
        }
    }

    /// <summary>The string value of <paramref name="name"/> on an object, or <c>null</c> when the element is not an object or the property is absent or not a string.</summary>
    public static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

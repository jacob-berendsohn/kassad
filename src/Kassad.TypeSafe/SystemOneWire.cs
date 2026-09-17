using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kassad.TypeSafe;

/// <summary>
/// Maps between Kassad abstractions and the <c>/v1/systemone</c> JSON wire format.
/// Hand-written rather than attribute-driven so the abstractions carry no serialization concerns
/// and so an out-of-order <c>type</c> discriminator never breaks parsing.
/// </summary>
internal static class SystemOneWire
{
    private static readonly JsonSerializerOptions StateOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static byte[] WriteRequest(DecisionRequest request, string model)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);

            writer.WritePropertyName("state");
            WriteState(writer, request.State);

            writer.WriteStartObject("questions");
            foreach (var (key, question) in request.Questions)
            {
                writer.WriteStartObject(key);
                WriteQuestion(writer, question);
                writer.WriteEndObject();
            }

            writer.WriteEndObject(); // questions
            writer.WriteEndObject(); // root
        }

        return buffer.ToArray();
    }

    public static async Task<DecisionResponse> ReadResponseAsync(
        Stream body,
        IReadOnlyDictionary<string, Question> questions,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new TypeSafeException("TypeSafe returned a response that is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String
                ? modelEl.GetString()!
                : "unknown";

            if (!root.TryGetProperty("answers", out var answersEl) || answersEl.ValueKind != JsonValueKind.Object)
            {
                throw new TypeSafeException("TypeSafe response is missing the 'answers' object.");
            }

            var answers = new Dictionary<string, Answer>(questions.Count, StringComparer.Ordinal);
            foreach (var (key, question) in questions)
            {
                if (!answersEl.TryGetProperty(key, out var answerEl))
                {
                    throw new TypeSafeException($"TypeSafe response has no answer for question '{key}'.");
                }

                answers[key] = ReadAnswer(key, question, answerEl);
            }

            var usage = ReadUsage(root);
            return new DecisionResponse(model, answers, usage);
        }
    }

    private static void WriteState(Utf8JsonWriter writer, object state)
    {
        switch (state)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case JsonNode node:
                node.WriteTo(writer);
                break;
            case ToolCallState toolCall:
                // The documented shape for tool-call policies (see ToolCallState). The schema and arguments arrive as
                // JSON text; they go out as JSON values so the model sees structure, not escaped strings.
                writer.WriteStartObject();
                writer.WriteString("user_intent", toolCall.UserIntent);
                writer.WriteString("tool_name", toolCall.ToolName);
                WriteJsonText(writer, "tool_schema", toolCall.ToolSchemaJson);
                WriteJsonText(writer, "arguments", toolCall.ArgumentsJson);
                writer.WriteEndObject();
                break;
            case GroundingState grounding:
                // The documented shape for grounding policies (see GroundingState); source_id only when the caller set one.
                writer.WriteStartObject();
                writer.WriteString("claim", grounding.Claim);
                writer.WriteString("source_passage", grounding.SourcePassage);
                if (grounding.SourceId is not null)
                {
                    writer.WriteString("source_id", grounding.SourceId);
                }

                writer.WriteEndObject();
                break;
            default:
                JsonSerializer.Serialize(writer, state, state.GetType(), StateOptions);
                break;
        }
    }

    /// <summary>
    /// Writes JSON text as a JSON value, or as a string when it does not parse, so an LLM's malformed arguments still
    /// reach the policies instead of failing the request.
    /// </summary>
    private static void WriteJsonText(Utf8JsonWriter writer, string name, string json)
    {
        writer.WritePropertyName(name);
        try
        {
            using var document = JsonDocument.Parse(json);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(json);
        }
    }

    private static void WriteQuestion(Utf8JsonWriter writer, Question question)
    {
        switch (question)
        {
            case NoulQuestion noul:
                writer.WriteString("type", "noul");
                writer.WriteString("instructions", noul.Instructions);
                if (noul.Criteria is { } c && (c.True is not null || c.False is not null))
                {
                    writer.WriteStartObject("criteria");
                    if (c.True is not null)
                    {
                        writer.WriteString("true", c.True);
                    }

                    if (c.False is not null)
                    {
                        writer.WriteString("false", c.False);
                    }

                    writer.WriteEndObject();
                }

                break;

            case ChoiceQuestion choice:
                writer.WriteString("type", "choice");
                writer.WriteString("instructions", choice.Instructions);
                writer.WriteStartObject("criteria");
                foreach (var (option, description) in choice.Options)
                {
                    if (description is null)
                    {
                        writer.WriteNull(option);
                    }
                    else
                    {
                        writer.WriteString(option, description);
                    }
                }

                writer.WriteEndObject();
                break;

            case ScoreQuestion score:
                writer.WriteString("type", "score");
                writer.WriteString("instructions", score.Instructions);
                writer.WriteStartArray("criteria");
                foreach (var level in score.Levels)
                {
                    writer.WriteStringValue(level);
                }

                writer.WriteEndArray();
                break;

            default:
                throw new TypeSafeException($"Unsupported question type '{question.GetType().Name}'.");
        }
    }

    private static Answer ReadAnswer(string key, Question question, JsonElement el)
    {
        var type = el.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString()
            : null;

        try
        {
            return (question, type) switch
            {
                (NoulQuestion, "noul") => new NoulAnswer(el.GetProperty("noul").GetDouble()),

                (ChoiceQuestion, "choice") => new ChoiceAnswer(
                    el.GetProperty("choice").GetString() ?? throw Malformed(key, "choice is null"),
                    ReadDoubleMap(el.GetProperty("probabilities")),
                    el.GetProperty("confidence").GetDouble()),

                (ScoreQuestion sq, "score") => ReadScore(sq, el),

                (_, null) => throw Malformed(key, "answer has no 'type'"),
                _ => throw Malformed(key, $"question is {question.GetType().Name} but answer type is '{type}'"),
            };
        }
        catch (KeyNotFoundException ex)
        {
            throw Malformed(key, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw Malformed(key, ex.Message);
        }
    }

    private static ScoreAnswer ReadScore(ScoreQuestion question, JsonElement el)
    {
        var score = el.GetProperty("score").GetDouble();
        var confidence = el.GetProperty("confidence").GetDouble();

        // Wire shape: legend { "0": "Calm", "1": "Frustrated" }, probabilities { "0": 0.1, "1": 0.9 }.
        // Order by numeric index rather than trusting property order.
        var probabilities = ReadDoubleMap(el.GetProperty("probabilities"))
            .OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
            .Select(kv => kv.Value)
            .ToArray();

        IReadOnlyList<string> levels = question.Levels;
        if (el.TryGetProperty("legend", out var legendEl) && legendEl.ValueKind == JsonValueKind.Object)
        {
            levels = legendEl.EnumerateObject()
                .OrderBy(p => int.Parse(p.Name, CultureInfo.InvariantCulture))
                .Select(p => p.Value.GetString() ?? string.Empty)
                .ToArray();
        }

        return new ScoreAnswer(score, levels, probabilities, confidence);
    }

    private static Dictionary<string, double> ReadDoubleMap(JsonElement el)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in el.EnumerateObject())
        {
            map[property.Name] = property.Value.GetDouble();
        }

        return map;
    }

    private static TokenUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new TokenUsage(0, 0);
        }

        return new TokenUsage(ReadInt(usage, "input_tokens"), ReadInt(usage, "output_tokens"));
    }

    private static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static TypeSafeException Malformed(string key, string detail) =>
        new($"TypeSafe answer for '{key}' is malformed: {detail}");
}

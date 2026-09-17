using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kassad.Policies;

/// <summary>
/// Wire shape of a policy file. Parsing is lenient (nulls everywhere) so that <see cref="PolicySet"/>
/// validation can report every problem in one pass instead of failing on the first missing field.
/// </summary>
internal sealed class PolicyDocument
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public List<PolicyEntry>? Policies { get; set; }

    public static IReadOnlyList<Policy> Parse(string json)
    {
        PolicyDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PolicyDocument>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PolicyValidationException([$"Policy file is not valid JSON: {ex.Message}"]);
        }

        if (document?.Policies is null)
        {
            throw new PolicyValidationException(["Policy file must have a top-level 'policies' array."]);
        }

        var errors = new List<string>();
        var policies = new List<Policy>(document.Policies.Count);

        for (var i = 0; i < document.Policies.Count; i++)
        {
            var entry = document.Policies[i];
            var label = string.IsNullOrWhiteSpace(entry.Id) ? $"policies[{i}]" : entry.Id;
            var errorsBefore = errors.Count;

            if (entry.Stage is null)
            {
                errors.Add($"{label}: 'stage' is required (inbound, outbound, tool_call, grounding).");
            }

            if (entry.OnError is null)
            {
                errors.Add($"{label}: 'on_error' is required (fail_open or fail_closed). There is no default.");
            }

            if (string.IsNullOrWhiteSpace(entry.Instructions))
            {
                errors.Add($"{label}: 'instructions' is required.");
            }

            if (entry.Actions is not null)
            {
                foreach (var (option, rule) in entry.Actions)
                {
                    if (rule?.Action is null)
                    {
                        errors.Add($"{label}: action for option '{option}' must specify 'action' (allow, flag, review, block).");
                    }
                }
            }

            var question = BuildQuestion(label, entry, errors);
            if (question is null || entry.Stage is null || entry.OnError is null || errors.Count > errorsBefore)
            {
                // Every problem with this entry is already in `errors`. Building a Policy from it anyway would
                // dereference the very pieces that are missing (a null stage, on_error, or action), so skip it;
                // the exception below reports the entry together with the rest of the document. The explicit null
                // checks are implied by the count check but are what lets the compiler see .Value is safe below.
                continue;
            }

            policies.Add(new Policy
            {
                Id = entry.Id ?? string.Empty,
                Stage = entry.Stage.Value,
                Question = question,
                OnError = entry.OnError.Value,
                Thresholds = entry.Thresholds is { } t ? new ActionThresholds(t.Flag, t.Review, t.Block) : null,
                ChoiceRules = entry.Actions?.ToDictionary(
                    kv => kv.Key,
                    kv => new ChoiceRule(kv.Value.Action!.Value, kv.Value.MinConfidence ?? 0),
                    StringComparer.Ordinal),
                MinConfidence = entry.MinConfidence ?? 0,
                Description = entry.Description,
            });
        }

        if (errors.Count > 0)
        {
            throw new PolicyValidationException(errors);
        }

        return policies;
    }

    private static Question? BuildQuestion(string label, PolicyEntry entry, List<string> errors)
    {
        var instructions = entry.Instructions ?? string.Empty;
        var criteria = entry.Criteria;

        switch (entry.Type?.ToLowerInvariant())
        {
            case "noul":
                if (criteria is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Undefined and not JsonValueKind.Null })
                {
                    errors.Add($"{label}: noul 'criteria' must be an object with optional 'true' and 'false' descriptions.");
                    return null;
                }

                NoulCriteria? noulCriteria = null;
                if (criteria is { ValueKind: JsonValueKind.Object } c)
                {
                    noulCriteria = new NoulCriteria(
                        c.TryGetProperty("true", out var t) ? t.GetString() : null,
                        c.TryGetProperty("false", out var f) ? f.GetString() : null);
                }

                return new NoulQuestion(instructions, noulCriteria);

            case "choice":
                if (criteria is not { ValueKind: JsonValueKind.Object } options)
                {
                    errors.Add($"{label}: choice 'criteria' must be an object of option → description.");
                    return null;
                }

                var map = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var property in options.EnumerateObject())
                {
                    map[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }

                if (map.Count < 2)
                {
                    errors.Add($"{label}: choice 'criteria' needs at least two options.");
                    return null;
                }

                return new ChoiceQuestion(instructions, map);

            case "score":
                if (criteria is not { ValueKind: JsonValueKind.Array } levelsEl)
                {
                    errors.Add($"{label}: score 'criteria' must be an ordered array of level descriptions.");
                    return null;
                }

                var levels = levelsEl.EnumerateArray().Select(l => l.GetString() ?? string.Empty).ToArray();
                if (levels.Length < 2)
                {
                    errors.Add($"{label}: score 'criteria' needs at least two levels.");
                    return null;
                }

                return new ScoreQuestion(instructions, levels);

            case null:
                errors.Add($"{label}: 'type' is required (noul, choice, score).");
                return null;

            default:
                errors.Add($"{label}: unknown type '{entry.Type}' (expected noul, choice, score).");
                return null;
        }
    }
}

internal sealed class PolicyEntry
{
    public string? Id { get; set; }

    public Stage? Stage { get; set; }

    public string? Type { get; set; }

    public string? Instructions { get; set; }

    public JsonElement? Criteria { get; set; }

    public ThresholdsEntry? Thresholds { get; set; }

    public Dictionary<string, ChoiceRuleEntry>? Actions { get; set; }

    public ErrorPolicy? OnError { get; set; }

    public double? MinConfidence { get; set; }

    public string? Description { get; set; }
}

internal sealed class ThresholdsEntry
{
    public double? Flag { get; set; }

    public double? Review { get; set; }

    public double? Block { get; set; }
}

internal sealed class ChoiceRuleEntry
{
    public VerdictAction? Action { get; set; }

    public double? MinConfidence { get; set; }
}

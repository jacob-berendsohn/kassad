using System.Text;
using System.Text.Json;

namespace Kassad.TypeSafe.Tests;

public class SystemOneWireTests
{
    private static readonly IReadOnlyDictionary<string, Question> Questions = new Dictionary<string, Question>
    {
        ["is_urgent"] = new NoulQuestion("Does this convey urgency?", new NoulCriteria(True: "Explicitly time-sensitive", False: "No urgency expressed")),
        ["department"] = new ChoiceQuestion("Which team should handle this?", new Dictionary<string, string?>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
            ["sales"] = null,
        }),
        ["frustration"] = new ScoreQuestion("How frustrated is the customer?", ["Calm", "Frustrated", "Very angry"]),
    };

    [Fact]
    public void Request_matches_documented_wire_shape()
    {
        var bytes = SystemOneWire.WriteRequest(new DecisionRequest("Help! My payouts have been failing for 3 days.", Questions), "jev-latest");
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        Assert.Equal("jev-latest", root.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("state").ValueKind);

        var q = root.GetProperty("questions");
        Assert.Equal("noul", q.GetProperty("is_urgent").GetProperty("type").GetString());
        Assert.Equal("Explicitly time-sensitive", q.GetProperty("is_urgent").GetProperty("criteria").GetProperty("true").GetString());

        Assert.Equal("choice", q.GetProperty("department").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, q.GetProperty("department").GetProperty("criteria").GetProperty("sales").ValueKind);

        Assert.Equal("score", q.GetProperty("frustration").GetProperty("type").GetString());
        Assert.Equal(3, q.GetProperty("frustration").GetProperty("criteria").GetArrayLength());
    }

    [Fact]
    public void Object_state_is_serialized_as_json_not_stringified()
    {
        var state = new { user = "u1", messages = new[] { "hi" } };
        var bytes = SystemOneWire.WriteRequest(new DecisionRequest(state, Questions), "jev-latest");
        using var doc = JsonDocument.Parse(bytes);

        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("state").ValueKind);
        Assert.Equal("u1", doc.RootElement.GetProperty("state").GetProperty("user").GetString());
    }

    [Fact]
    public async Task Response_parses_all_three_answer_types_regardless_of_property_order()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response-all-types.json"));

        var response = await SystemOneWire.ReadResponseAsync(stream, Questions, CancellationToken.None);

        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(312, response.Usage.InputTokens);

        var noul = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.Equal(0.92, noul.Probability);

        var choice = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Equal("technical", choice.Choice);
        Assert.Equal(0.85, choice.Probabilities["technical"]);
        Assert.Equal(0.82, choice.Confidence);

        var score = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
        Assert.Equal(1.6, score.Score);
        Assert.Equal(new[] { "Calm", "Frustrated", "Very angry" }, score.Levels);      // legend re-ordered by index
        Assert.Equal(new[] { 0.05, 0.3, 0.65 }, score.Probabilities);                  // probabilities re-ordered by index
    }

    [Fact]
    public async Task Missing_answer_for_a_question_is_a_malformed_response()
    {
        var json = """{ "model": "jev-latest", "answers": { "is_urgent": { "type": "noul", "noul": 0.1 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }""";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var ex = await Assert.ThrowsAsync<TypeSafeException>(() => SystemOneWire.ReadResponseAsync(stream, Questions, CancellationToken.None));
        Assert.Contains("department", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_type_mismatch_is_a_malformed_response()
    {
        var questions = new Dictionary<string, Question> { ["q"] = new NoulQuestion("?") };
        var json = """{ "model": "jev-latest", "answers": { "q": { "type": "choice", "choice": "a", "probabilities": { "a": 1 }, "confidence": 1 } } }""";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<TypeSafeException>(() => SystemOneWire.ReadResponseAsync(stream, questions, CancellationToken.None));
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Kassad.TypeSafe.Tests;

public class SystemOneWireTests
{
    private static IReadOnlyDictionary<string, Question> Questions => FixtureRequests.AllTypes.Questions;

    [Fact]
    public void Request_matches_documented_wire_shape()
    {
        var bytes = SystemOneWire.WriteRequest(FixtureRequests.AllTypes, "jev-latest");
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
    public async Task Recorded_response_round_trips_every_primitive()
    {
        // The fixture is a live response to FixtureRequests.AllTypes (see FixtureRecorder). Expected values are
        // read from the same file, so re-recording never requires editing this test.
        var path = FixtureRequests.Resolve(FixtureRequests.AllTypesFile);
        using var raw = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = raw.RootElement;
        await using var stream = File.OpenRead(path);

        var response = await SystemOneWire.ReadResponseAsync(stream, Questions, CancellationToken.None);

        Assert.Equal(root.GetProperty("model").GetString(), response.Model);
        Assert.StartsWith("jev-", response.Model, StringComparison.Ordinal); // a resolved release such as jev-1.13.0, not the alias
        Assert.Equal(root.GetProperty("usage").GetProperty("input_tokens").GetInt32(), response.Usage.InputTokens);
        Assert.Equal(root.GetProperty("usage").GetProperty("output_tokens").GetInt32(), response.Usage.OutputTokens);
        Assert.True(response.Usage.InputTokens > 0);

        var answers = root.GetProperty("answers");

        var noul = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.Equal(answers.GetProperty("is_urgent").GetProperty("noul").GetDouble(), noul.Probability);

        var choiceEl = answers.GetProperty("department");
        var choice = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Equal(choiceEl.GetProperty("choice").GetString(), choice.Choice);
        Assert.Equal(choiceEl.GetProperty("confidence").GetDouble(), choice.Confidence);
        Assert.Equal(3, choice.Probabilities.Count);
        foreach (var option in choiceEl.GetProperty("probabilities").EnumerateObject())
        {
            Assert.Equal(option.Value.GetDouble(), choice.Probabilities[option.Name]);
        }

        var scoreEl = answers.GetProperty("frustration");
        var score = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
        Assert.Equal(scoreEl.GetProperty("score").GetDouble(), score.Score);
        Assert.Equal(scoreEl.GetProperty("confidence").GetDouble(), score.Confidence);
        Assert.Equal(((ScoreQuestion)Questions["frustration"]).Levels, score.Levels); // legend echoes the levels we sent
        Assert.Equal(
            scoreEl.GetProperty("probabilities").EnumerateObject()
                .OrderBy(p => int.Parse(p.Name, CultureInfo.InvariantCulture))
                .Select(p => p.Value.GetDouble()),
            score.Probabilities);
    }

    [Fact]
    public void Every_fixture_was_recorded_from_the_live_api()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.json");

        Assert.Equal(FixtureRequests.All.Count, files.Length);
        Assert.All(files, file =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var recordedAt = doc.RootElement.GetProperty("recorded_at").GetString();
            Assert.True(
                DateTimeOffset.TryParse(recordedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _),
                $"{Path.GetFileName(file)}: recorded_at '{recordedAt}' is not a timestamp");
            Assert.InRange(doc.RootElement.GetProperty("recorded_status").GetInt32(), 200, 599);
        });
    }

    [Fact]
    public async Task Answers_parse_regardless_of_property_order()
    {
        // Hand-scrambled: type after the payload, legend and probabilities keyed out of order.
        const string json = """
            {
              "model": "jev-latest",
              "answers": {
                "is_urgent": { "noul": 0.92, "type": "noul" },
                "department": { "choice": "technical", "probabilities": { "billing": 0.08, "technical": 0.85, "sales": 0.07 }, "confidence": 0.82, "type": "choice" },
                "frustration": { "type": "score", "score": 1.6, "legend": { "1": "Frustrated", "0": "Calm", "2": "Very angry" }, "probabilities": { "2": 0.65, "0": 0.05, "1": 0.3 }, "confidence": 0.78 }
              },
              "usage": { "input_tokens": 312, "output_tokens": 48 }
            }
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var response = await SystemOneWire.ReadResponseAsync(stream, Questions, CancellationToken.None);

        Assert.Equal(0.92, Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]).Probability);
        var choice = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Equal("technical", choice.Choice);
        Assert.Equal(0.82, choice.Confidence);
        var score = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
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

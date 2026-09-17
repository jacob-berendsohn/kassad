namespace Kassad.TypeSafe.Tests;

/// <summary>
/// Round-trips each primitive through <see cref="TypeSafeClient"/> against <c>api.typesafe.ai</c> and pins
/// the error split the API actually uses. Skipped without <c>TYPESAFE_API_KEY</c>; excluded from the
/// no-network CI job by the Live trait and run by the <c>live</c> job on pushes to <c>main</c>.
/// </summary>
[Trait("Category", "Live")]
[Collection(ApiKeyEnvironmentCollection.Name)]
public class TypeSafeLiveTests
{
    private const string State = "Help! My payouts have been failing for 3 days and nobody has answered my ticket.";

    [LiveFact]
    public async Task Noul_answer_is_a_probability()
    {
        using var http = new HttpClient();
        var questions = new Dictionary<string, Question>
        {
            ["is_urgent"] = new NoulQuestion("Does this convey urgency?", new NoulCriteria(True: "Explicitly time-sensitive", False: "No urgency expressed")),
        };

        var response = await LiveApi.CreateClient(http).EvaluateAsync(new DecisionRequest(State, questions));

        var answer = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.InRange(answer.Probability, 0d, 1d);
        Assert.StartsWith("jev-", response.Model, StringComparison.Ordinal); // the resolved release, not the alias we sent
    }

    [LiveFact]
    public async Task Choice_answer_is_the_most_probable_option_of_a_distribution_over_our_options()
    {
        using var http = new HttpClient();
        var options = new Dictionary<string, string?>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
            ["sales"] = null,
        };
        var questions = new Dictionary<string, Question> { ["department"] = new ChoiceQuestion("Which team should handle this?", options) };

        var response = await LiveApi.CreateClient(http).EvaluateAsync(new DecisionRequest(State, questions));

        var answer = Assert.IsType<ChoiceAnswer>(response.Answers["department"]);
        Assert.Contains(answer.Choice, options.Keys);
        Assert.Equal(options.Keys.Order(), answer.Probabilities.Keys.Order());
        Assert.Equal(1d, answer.Probabilities.Values.Sum(), 0.02); // wire values are rounded to two decimals
        Assert.Equal(answer.Probabilities.MaxBy(kv => kv.Value).Key, answer.Choice);
        Assert.InRange(answer.Confidence, 0d, 1d);
    }

    [LiveFact]
    public async Task Score_answer_is_the_probability_weighted_level_index()
    {
        using var http = new HttpClient();
        string[] levels = ["Calm", "Frustrated", "Very angry"];
        var questions = new Dictionary<string, Question> { ["frustration"] = new ScoreQuestion("How frustrated is the customer?", levels) };

        var response = await LiveApi.CreateClient(http).EvaluateAsync(new DecisionRequest(State, questions));

        var answer = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
        Assert.Equal(levels, answer.Levels); // the legend echoes our levels, in order
        Assert.Equal(levels.Length, answer.Probabilities.Count);
        Assert.Equal(1d, answer.Probabilities.Sum(), 0.02);
        Assert.InRange(answer.Score, 0d, levels.Length - 1);
        Assert.Equal(answer.Probabilities.Select((p, i) => p * i).Sum(), answer.Score, 0.05);
        Assert.InRange(answer.Confidence, 0d, 1d);
    }

    [LiveFact]
    public async Task Usage_is_populated()
    {
        using var http = new HttpClient();
        var questions = new Dictionary<string, Question> { ["is_urgent"] = new NoulQuestion("Does this convey urgency?") };

        var response = await LiveApi.CreateClient(http).EvaluateAsync(new DecisionRequest(State, questions));

        Assert.True(response.Usage.InputTokens > 0, "input_tokens should be counted");
        Assert.True(response.Usage.OutputTokens > 0, "output_tokens should be counted");
    }

    [LiveFact]
    public async Task Schema_violation_is_a_422_request_exception_and_is_not_retried()
    {
        using var counting = new CountingHandler();
        using var http = new HttpClient(counting, disposeHandler: false);
        var client = LiveApi.CreateClient(http);

        var ex = await Assert.ThrowsAsync<TypeSafeRequestException>(() => client.EvaluateAsync(FixtureRequests.NumericState));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("\"state\"", ex.ResponseBody, StringComparison.Ordinal); // detail[].loc names the offending field
        Assert.Equal(1, counting.Requests);
    }

    [LiveFact]
    public async Task Semantic_violation_is_a_400_request_exception_and_is_not_retried()
    {
        using var counting = new CountingHandler();
        using var http = new HttpClient(counting, disposeHandler: false);
        var client = LiveApi.CreateClient(http);

        var ex = await Assert.ThrowsAsync<TypeSafeRequestException>(() => client.EvaluateAsync(FixtureRequests.EmptyInstructions));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("instructions", ex.ResponseBody, StringComparison.Ordinal);
        Assert.Equal(1, counting.Requests);
    }

    /// <summary>Counts requests that reach the network so a test can prove the client did not retry.</summary>
    private sealed class CountingHandler : DelegatingHandler
    {
        public CountingHandler() : base(new HttpClientHandler())
        {
        }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return base.SendAsync(request, cancellationToken);
        }
    }
}

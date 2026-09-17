using System.Net;

namespace Kassad.TypeSafe.Tests;

// In the API-key collection because Missing_api_key_fails_at_construction_not_first_call clears TYPESAFE_API_KEY
// for the process; it must not overlap a live test that is reading it.
[Collection(ApiKeyEnvironmentCollection.Name)]
public class TypeSafeClientTests
{
    private const string Ok = """{ "model": "jev-latest", "answers": { "q": { "type": "noul", "noul": 0.5 } }, "usage": { "input_tokens": 5, "output_tokens": 1 } }""";

    private static DecisionRequest Request() => new("state", new Dictionary<string, Question> { ["q"] = new NoulQuestion("?") });

    [Fact]
    public async Task Sends_bearer_auth_and_json_to_v1_systemone()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, Ok);
        var client = StubHandler.Client(handler);

        var response = await client.EvaluateAsync(Request());

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", sent.RequestUri!.ToString());
        Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", sent.Headers.Authorization.Parameter);
        Assert.Equal("application/json", sent.Content!.Headers.ContentType!.MediaType);
        Assert.Equal(0.5, Assert.IsType<NoulAnswer>(response.Answers["q"]).Probability);
        Assert.Equal("typesafe:jev-latest", client.Name);
    }

    [Fact]
    public async Task Retries_429_and_529_then_succeeds()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""")
            .Enqueue((HttpStatusCode)529, """{"error":"overloaded"}""")
            .Enqueue(HttpStatusCode.OK, Ok);
        var client = StubHandler.Client(handler, maxRetries: 3);

        await client.EvaluateAsync(Request());

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Exhausted_retries_surface_as_rate_limit_exception_with_retry_after()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, "{}", r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(7)))
            .Enqueue(HttpStatusCode.TooManyRequests, "{}", r => r.Headers.RetryAfter = new(TimeSpan.FromSeconds(7)));
        var client = StubHandler.Client(handler, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<TypeSafeRateLimitException>(() => client.EvaluateAsync(Request()));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Auth_failures_are_not_retried()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        var client = StubHandler.Client(handler);

        var ex = await Assert.ThrowsAsync<TypeSafeAuthenticationException>(() => client.EvaluateAsync(Request()));

        Assert.Equal(401, ex.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Schema_failures_are_422_request_exceptions_not_retried_and_carry_the_body()
    {
        // Recorded body: a pydantic-style "detail" array whose "loc" names the offending field.
        var body = await File.ReadAllTextAsync(FixtureRequests.Resolve(FixtureRequests.NumericStateFile));
        var handler = new StubHandler().Enqueue(HttpStatusCode.UnprocessableEntity, body);
        var client = StubHandler.Client(handler);

        var ex = await Assert.ThrowsAsync<TypeSafeRequestException>(() => client.EvaluateAsync(Request()));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("\"loc\"", ex.ResponseBody, StringComparison.Ordinal);
        Assert.Contains("\"state\"", ex.ResponseBody, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Semantic_failures_are_400_request_exceptions_not_retried_and_carry_the_body()
    {
        // Recorded body: a "detail" string. The API uses 400, not 422, for rules its schema cannot express.
        var body = await File.ReadAllTextAsync(FixtureRequests.Resolve(FixtureRequests.EmptyInstructionsFile));
        var handler = new StubHandler().Enqueue(HttpStatusCode.BadRequest, body);
        var client = StubHandler.Client(handler);

        var ex = await Assert.ThrowsAsync<TypeSafeRequestException>(() => client.EvaluateAsync(Request()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("instructions", ex.ResponseBody, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Missing_api_key_fails_at_construction_not_first_call()
    {
        var previous = Environment.GetEnvironmentVariable(TypeSafeClientOptions.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(TypeSafeClientOptions.ApiKeyEnvironmentVariable, null);
        try
        {
            Assert.Throws<InvalidOperationException>(() => new TypeSafeClient(new HttpClient(new StubHandler()), new TypeSafeClientOptions()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TypeSafeClientOptions.ApiKeyEnvironmentVariable, previous);
        }
    }
}

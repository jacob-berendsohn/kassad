namespace Kassad.Tests;

/// <summary>
/// Scripted <see cref="IDecisionModel"/>. Returns the configured answer per question key, or throws.
/// Records every request so tests can assert on batching.
/// </summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    private readonly Dictionary<string, Answer> _answers = new(StringComparer.Ordinal);

    public string Name => "fake";

    public Exception? ThrowOnEvaluate { get; set; }

    public List<DecisionRequest> Requests { get; } = [];

    public FakeDecisionModel Answer(string key, Answer answer)
    {
        _answers[key] = answer;
        return this;
    }

    public Task<DecisionResponse> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (ThrowOnEvaluate is not null)
        {
            throw ThrowOnEvaluate;
        }

        var answers = request.Questions.Keys
            .Where(_answers.ContainsKey)
            .ToDictionary(k => k, k => _answers[k], StringComparer.Ordinal);

        return Task.FromResult(new DecisionResponse("fake", answers, new TokenUsage(10, 1)));
    }
}

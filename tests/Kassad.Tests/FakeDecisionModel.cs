namespace Kassad.Tests;

/// <summary>
/// Scripted <see cref="IDecisionModel"/>. Returns the configured answer per question key, or throws.
/// Records every request so tests can assert on batching, and can take its time like a slow model.
/// </summary>
public sealed class FakeDecisionModel : IDecisionModel
{
    private readonly Dictionary<string, Answer> _answers = new(StringComparer.Ordinal);

    public string Name => "fake";

    public Exception? ThrowOnEvaluate { get; set; }

    /// <summary>How long to wait before answering. Honors the cancellation token, as a real client does.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>
    /// When true, cancellation during <see cref="Delay"/> surfaces as a <see cref="DecisionModelException"/> wrapping
    /// the <see cref="OperationCanceledException"/>, like a client that wraps every failure.
    /// </summary>
    public bool WrapCancellation { get; set; }

    /// <summary>The token the most recent request was evaluated with.</summary>
    public CancellationToken LastToken { get; private set; }

    public List<DecisionRequest> Requests { get; } = [];

    public FakeDecisionModel Answer(string key, Answer answer)
    {
        _answers[key] = answer;
        return this;
    }

    public async Task<DecisionResponse> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        LastToken = cancellationToken;
        if (ThrowOnEvaluate is not null)
        {
            throw ThrowOnEvaluate;
        }

        if (Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(Delay, cancellationToken);
            }
            catch (OperationCanceledException ex) when (WrapCancellation)
            {
                throw new DecisionModelException("cancelled mid-request", ex);
            }
        }

        var answers = request.Questions.Keys
            .Where(_answers.ContainsKey)
            .ToDictionary(k => k, k => _answers[k], StringComparer.Ordinal);

        return new DecisionResponse("fake", answers, new TokenUsage(10, 1));
    }
}

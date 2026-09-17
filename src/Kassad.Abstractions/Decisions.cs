namespace Kassad;

/// <summary>
/// One evaluation call: a state plus a map of independent questions.
/// Every question is evaluated in isolation against the same state, so batching is free and
/// questions do not contaminate each other.
/// </summary>
/// <param name="State">The content to evaluate. A <see cref="string"/>, or any object the decision model can serialize (chat transcript, record, application state).</param>
/// <param name="Questions">Caller-chosen key → question. Answers come back under the same keys.</param>
public sealed record DecisionRequest(object State, IReadOnlyDictionary<string, Question> Questions)
{
    /// <summary>The content to evaluate.</summary>
    public object State { get; init; } = State ?? throw new ArgumentNullException(nameof(State));

    /// <summary>Caller-chosen key → question. Must contain at least one entry.</summary>
    public IReadOnlyDictionary<string, Question> Questions { get; init; } =
        Questions is { Count: > 0 } ? Questions : throw new ArgumentException("At least one question is required.", nameof(Questions));
}

/// <summary>The result of a <see cref="DecisionRequest"/>: one answer per question key.</summary>
public sealed record DecisionResponse(string Model, IReadOnlyDictionary<string, Answer> Answers, TokenUsage Usage);

/// <summary>Token accounting for one evaluation.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens);

/// <summary>
/// A model that turns a state and typed questions into typed, probabilistic answers.
/// Implemented by <c>Kassad.TypeSafe</c> for TypeSafe's System One API; implement it yourself to
/// back Kassad with a different provider, or with a fake in tests.
/// </summary>
public interface IDecisionModel
{
    /// <summary>Stable identifier for logs and metrics, e.g. <c>typesafe:jev-latest</c>.</summary>
    string Name { get; }

    /// <summary>Evaluate every question in <paramref name="request"/> against its state.</summary>
    /// <exception cref="DecisionModelException">The provider could not produce an answer (transport, auth, rate limit, malformed response).</exception>
    Task<DecisionResponse> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Base type for failures raised by an <see cref="IDecisionModel"/>. The engine treats these as "model unavailable" and applies the policy's error behavior.</summary>
public class DecisionModelException : Exception
{
    /// <inheritdoc cref="Exception()"/>
    public DecisionModelException()
    {
    }

    /// <inheritdoc cref="Exception(string)"/>
    public DecisionModelException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    public DecisionModelException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

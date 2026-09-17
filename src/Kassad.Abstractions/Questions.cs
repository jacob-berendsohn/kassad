namespace Kassad;

/// <summary>
/// A typed question evaluated against a state by an <see cref="IDecisionModel"/>.
/// Exactly one of <see cref="NoulQuestion"/>, <see cref="ChoiceQuestion"/>, or <see cref="ScoreQuestion"/>.
/// </summary>
/// <remarks>
/// Each question should ask one narrow thing. If a judgment needs several factors, ask several
/// questions and combine the answers in code. Instructions are natural language; the model does
/// not see the question's key, only its instructions and criteria.
/// </remarks>
public abstract record Question(string Instructions)
{
    /// <summary>The natural-language question or instruction the model evaluates.</summary>
    public string Instructions { get; init; } = Instructions ?? throw new ArgumentNullException(nameof(Instructions));
}

/// <summary>A yes/no question. The answer is the probability that the answer is yes.</summary>
public sealed record NoulQuestion(string Instructions, NoulCriteria? Criteria = null) : Question(Instructions);

/// <summary>Optional descriptions of what a yes and a no mean for a <see cref="NoulQuestion"/>.</summary>
public sealed record NoulCriteria(string? True = null, string? False = null);

/// <summary>Select one option from a defined set. The answer carries a probability per option plus a confidence.</summary>
public sealed record ChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string?> Options) : Question(Instructions)
{
    /// <summary>Option key → rubric description. Use <c>null</c> when an option needs no description.</summary>
    public IReadOnlyDictionary<string, string?> Options { get; init; } =
        Options is { Count: >= 2 } ? Options : throw new ArgumentException("A Choice question needs at least two options.", nameof(Options));
}

/// <summary>Rate the state against ordered levels. The answer is a probability-weighted value across the levels plus a confidence.</summary>
public sealed record ScoreQuestion(string Instructions, IReadOnlyList<string> Levels) : Question(Instructions)
{
    /// <summary>Ordered level descriptions, lowest first. Score 0 corresponds to the first level.</summary>
    public IReadOnlyList<string> Levels { get; init; } =
        Levels is { Count: >= 2 } ? Levels : throw new ArgumentException("A Score question needs at least two levels.", nameof(Levels));
}

namespace Kassad;

/// <summary>The typed result of evaluating a <see cref="Question"/>.</summary>
public abstract record Answer;

/// <summary>Answer to a <see cref="NoulQuestion"/>.</summary>
/// <param name="Probability">Probability (0–1) that the answer is yes. Noul answers carry no separate confidence; threshold on this value directly.</param>
public sealed record NoulAnswer(double Probability) : Answer;

/// <summary>Answer to a <see cref="ChoiceQuestion"/>.</summary>
/// <param name="Choice">The highest-probability option key.</param>
/// <param name="Probabilities">Option key → probability. Sums to 1.</param>
/// <param name="Confidence">0–1. Derived from the shape of <paramref name="Probabilities"/>; flatter distribution = lower confidence.</param>
public sealed record ChoiceAnswer(string Choice, IReadOnlyDictionary<string, double> Probabilities, double Confidence) : Answer;

/// <summary>Answer to a <see cref="ScoreQuestion"/>.</summary>
/// <param name="Score">Probability-weighted value across the levels. 0 = first level, N-1 = last level; may land between levels.</param>
/// <param name="Levels">The level descriptions, in order, as the model saw them.</param>
/// <param name="Probabilities">Probability per level, same order as <paramref name="Levels"/>. Sums to 1.</param>
/// <param name="Confidence">0–1. Derived from the shape of <paramref name="Probabilities"/>.</param>
public sealed record ScoreAnswer(double Score, IReadOnlyList<string> Levels, IReadOnlyList<double> Probabilities, double Confidence) : Answer;

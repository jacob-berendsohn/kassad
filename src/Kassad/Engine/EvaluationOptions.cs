namespace Kassad.Engine;

/// <summary>
/// Operator settings for <see cref="GuardrailEngine"/>. Policies decide verdicts; these options bound how the
/// engine obtains them. Configure through <c>AddKassad(..., configure)</c>, or bind the section from
/// configuration with <c>services.Configure&lt;EvaluationOptions&gt;(...)</c>.
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>
    /// Longest a stage evaluation waits on the decision model. When the budget runs out the model call is
    /// cancelled and every policy for the stage resolves through its <see cref="ErrorPolicy"/>: <c>fail_closed</c>
    /// blocks, <c>fail_open</c> allows, and <see cref="StageResult.BudgetExceeded"/> is set on the result. The
    /// caller's own <see cref="CancellationToken"/> still propagates as <see cref="OperationCanceledException"/>.
    /// <c>null</c>, the default, means no budget: the engine waits as long as the model does.
    /// </summary>
    /// <remarks>
    /// The budget is enforced through the token handed to <see cref="IDecisionModel.EvaluateAsync"/>, so a model
    /// that ignores cancellation is not cut off. <c>Kassad.TypeSafe</c> honors it, including during retry backoff.
    /// </remarks>
    public TimeSpan? Budget { get; set; }

    /// <summary>Largest accepted <see cref="Budget"/>: the longest delay a <see cref="CancellationTokenSource"/> timer supports.</summary>
    internal static readonly TimeSpan MaxBudget = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    internal const string BudgetRule = "Budget must be positive and at most 49 days, or null for no budget.";

    internal static bool IsValidBudget(TimeSpan? budget) => budget is null || (budget > TimeSpan.Zero && budget <= MaxBudget);
}

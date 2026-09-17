namespace Kassad.Engine;

/// <summary>Evaluates every policy for a stage against a state in one decision-model call and returns the verdicts.</summary>
public interface IGuardrailEngine
{
    /// <summary>
    /// Run all policies registered for <paramref name="stage"/> against <paramref name="state"/>.
    /// Never throws for model failures; those become verdicts via each policy's <see cref="ErrorPolicy"/>.
    /// Throws <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> is signaled.
    /// </summary>
    Task<StageResult> EvaluateAsync(Stage stage, object state, CancellationToken cancellationToken = default);
}

namespace Kassad.Engine;

/// <summary>
/// Typed entry points for the two stages that have no framework hook. Each fixes the <see cref="Stage"/> and the
/// state type together, so a <see cref="GroundingState"/> cannot be evaluated against tool-call policies by mistake.
/// Otherwise they are <see cref="IGuardrailEngine.EvaluateAsync"/>: a <see cref="ToolCallState"/> or
/// <see cref="GroundingState"/> handed to it directly reaches the model in the same shape.
/// </summary>
public static class GuardrailEngineExtensions
{
    /// <summary>
    /// Run every <see cref="Stage.ToolCall"/> policy against a tool call the LLM has proposed, before it is executed.
    /// The model sees <paramref name="toolCall"/> as <c>{ user_intent, tool_name, tool_schema, arguments }</c>.
    /// </summary>
    /// <param name="engine">The engine holding the policies.</param>
    /// <param name="toolCall">The proposed call and the user request behind it.</param>
    /// <param name="cancellationToken">The caller's token; its cancellation propagates as <see cref="OperationCanceledException"/>.</param>
    /// <returns>One verdict per tool-call policy and their combined outcome; <see cref="VerdictAction.Allow"/> with no verdicts when no policy is registered for the stage.</returns>
    public static Task<StageResult> EvaluateToolCallAsync(this IGuardrailEngine engine, ToolCallState toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(toolCall);
        return engine.EvaluateAsync(Stage.ToolCall, toolCall, cancellationToken);
    }

    /// <summary>
    /// Run every <see cref="Stage.Grounding"/> policy against one claim and the passage it cites.
    /// The model sees <paramref name="grounding"/> as <c>{ claim, source_passage, source_id }</c>, the last only when set.
    /// </summary>
    /// <param name="engine">The engine holding the policies.</param>
    /// <param name="grounding">The claim and its source.</param>
    /// <param name="cancellationToken">The caller's token; its cancellation propagates as <see cref="OperationCanceledException"/>.</param>
    /// <returns>One verdict per grounding policy and their combined outcome; <see cref="VerdictAction.Allow"/> with no verdicts when no policy is registered for the stage.</returns>
    public static Task<StageResult> EvaluateGroundingAsync(this IGuardrailEngine engine, GroundingState grounding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(grounding);
        return engine.EvaluateAsync(Stage.Grounding, grounding, cancellationToken);
    }
}

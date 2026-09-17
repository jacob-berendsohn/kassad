namespace Kassad;

/// <summary>
/// State for the <see cref="Stage.ToolCall"/> stage: a tool call the LLM has proposed, captured before it runs,
/// together with what the user asked for. Evaluate it with <c>IGuardrailEngine.EvaluateToolCallAsync</c>.
/// </summary>
/// <remarks>
/// A decision model presents this state to its model as an object with four fields, and policy instructions can
/// refer to them by name: <c>user_intent</c>, <c>tool_name</c>, <c>tool_schema</c> and <c>arguments</c>.
/// <c>Kassad.TypeSafe</c> writes <see cref="ToolSchemaJson"/> and <see cref="ArgumentsJson"/> as JSON values when they
/// parse, so the model sees structure rather than escaped text, and as plain strings when they do not: an LLM can
/// emit malformed arguments, and the policies still get to see them.
/// </remarks>
/// <param name="UserIntent">What the user asked for, in their words: the message or request that led the LLM to propose this call.</param>
/// <param name="ToolName">The name of the tool or function the LLM wants to call.</param>
/// <param name="ToolSchemaJson">The tool's definition as the LLM was given it, as JSON text: typically the parameter schema together with its description.</param>
/// <param name="ArgumentsJson">The arguments the LLM proposed, as the JSON text it produced.</param>
public sealed record ToolCallState(string UserIntent, string ToolName, string ToolSchemaJson, string ArgumentsJson)
{
    /// <summary>What the user asked for, in their words.</summary>
    public string UserIntent { get; init; } = UserIntent ?? throw new ArgumentNullException(nameof(UserIntent));

    /// <summary>The name of the tool or function the LLM wants to call.</summary>
    public string ToolName { get; init; } = ToolName ?? throw new ArgumentNullException(nameof(ToolName));

    /// <summary>The tool's definition as the LLM was given it, as JSON text.</summary>
    public string ToolSchemaJson { get; init; } = ToolSchemaJson ?? throw new ArgumentNullException(nameof(ToolSchemaJson));

    /// <summary>The arguments the LLM proposed, as the JSON text it produced.</summary>
    public string ArgumentsJson { get; init; } = ArgumentsJson ?? throw new ArgumentNullException(nameof(ArgumentsJson));
}

/// <summary>
/// State for the <see cref="Stage.Grounding"/> stage: one claim paired with the source passage it cites, so a policy
/// can ask whether the source supports the claim. Evaluate it with <c>IGuardrailEngine.EvaluateGroundingAsync</c>.
/// </summary>
/// <remarks>
/// A decision model presents this state to its model as an object with the fields <c>claim</c>, <c>source_passage</c>
/// and, when set, <c>source_id</c>; policy instructions can refer to them by name.
/// </remarks>
/// <param name="Claim">The statement to check: one sentence or assertion, not a whole answer. Check several claims with several calls.</param>
/// <param name="SourcePassage">The text the claim cites, verbatim. Only this passage counts as support; the model is not asked what else might be true.</param>
/// <param name="SourceId">Optional identifier of where the passage came from (document id, URL, chunk key), for logs and for the model to see. Left out of the state when <c>null</c>.</param>
public sealed record GroundingState(string Claim, string SourcePassage, string? SourceId = null)
{
    /// <summary>The statement to check.</summary>
    public string Claim { get; init; } = Claim ?? throw new ArgumentNullException(nameof(Claim));

    /// <summary>The text the claim cites, verbatim.</summary>
    public string SourcePassage { get; init; } = SourcePassage ?? throw new ArgumentNullException(nameof(SourcePassage));
}

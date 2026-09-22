namespace Kassad.Eval;

/// <summary>
/// The spellings the results file uses for enums and question types: the policy-file and <c>Kassad-Outcome</c>
/// spellings, the same ones the telemetry tags carry (Docs/specs/telemetry.md), never the C# names.
/// </summary>
internal static class Names
{
    public static string StageName(Stage stage) => stage switch
    {
        Stage.Inbound => "inbound",
        Stage.Outbound => "outbound",
        Stage.ToolCall => "tool_call",
        Stage.Grounding => "grounding",
        _ => stage.ToString().ToLowerInvariant(),
    };

    public static string ActionName(VerdictAction action) => action switch
    {
        VerdictAction.Allow => "allow",
        VerdictAction.Flag => "flag",
        VerdictAction.Review => "review",
        VerdictAction.Block => "block",
        _ => action.ToString().ToLowerInvariant(),
    };

    public static string OnErrorName(ErrorPolicy onError) => onError == ErrorPolicy.FailClosed ? "fail_closed" : "fail_open";

    public static string QuestionTypeName(Question question) => question switch
    {
        NoulQuestion => "noul",
        ChoiceQuestion => "choice",
        ScoreQuestion => "score",
        _ => question.GetType().Name,
    };

    /// <summary>Every action, in severity order, so histograms list them the same way every run.</summary>
    public static IReadOnlyList<string> AllActions { get; } = [ActionName(VerdictAction.Allow), ActionName(VerdictAction.Flag), ActionName(VerdictAction.Review), ActionName(VerdictAction.Block)];
}

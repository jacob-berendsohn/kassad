namespace Kassad.TypeSafe.Tests;

/// <summary>
/// The requests that produced <c>Fixtures/*.json</c>. <see cref="FixtureRecorder"/> sends exactly these
/// through <see cref="SystemOneWire.WriteRequest"/> and stores the raw responses; the unit tests read the
/// files back against the same questions. Change a request here and re-record.
/// </summary>
internal static class FixtureRequests
{
    /// <summary>One question per primitive against a plain-string state. Expected: 200.</summary>
    public const string AllTypesFile = "response-all-types.json";

    /// <summary>A numeric state, which the API's schema rejects. Expected: 422 with a <c>detail</c> array naming <c>state</c>.</summary>
    public const string NumericStateFile = "response-422-numeric-state.json";

    /// <summary>A noul question with empty instructions and no criteria. Expected: 400 with a <c>detail</c> string.</summary>
    public const string EmptyInstructionsFile = "response-400-empty-instructions.json";

    public static readonly DecisionRequest AllTypes = new(
        "Help! My payouts have been failing for 3 days.",
        new Dictionary<string, Question>
        {
            ["is_urgent"] = new NoulQuestion("Does this convey urgency?", new NoulCriteria(True: "Explicitly time-sensitive", False: "No urgency expressed")),
            ["department"] = new ChoiceQuestion("Which team should handle this?", new Dictionary<string, string?>
            {
                ["billing"] = "Payments, invoicing, refunds",
                ["technical"] = "Bugs, outages, integrations",
                ["sales"] = null,
            }),
            ["frustration"] = new ScoreQuestion("How frustrated is the customer?", ["Calm", "Frustrated", "Very angry"]),
        });

    /// <summary>Nothing in the abstractions stops a caller passing a number as the state; the API does.</summary>
    public static readonly DecisionRequest NumericState = new(
        42,
        new Dictionary<string, Question> { ["q"] = new NoulQuestion("Is this a greeting?") });

    public static readonly DecisionRequest EmptyInstructions = new(
        "hello",
        new Dictionary<string, Question> { ["q"] = new NoulQuestion(string.Empty) });

    /// <summary>Every fixture, in recording order, with the HTTP status the recorder insists on before writing.</summary>
    public static readonly IReadOnlyList<(string File, DecisionRequest Request, int ExpectedStatus)> All =
    [
        (AllTypesFile, AllTypes, 200),
        (NumericStateFile, NumericState, 422),
        (EmptyInstructionsFile, EmptyInstructions, 400),
    ];

    /// <summary>Path of a fixture in the test output directory (copied from the source tree at build).</summary>
    public static string Resolve(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
}

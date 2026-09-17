using Kassad.Policies;

namespace Kassad.Tests;

public class PolicySetTests
{
    [Fact]
    public void Valid_policies_index_by_stage()
    {
        var set = PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.RequestClass(), TestPolicies.HarmSeverity()]);

        Assert.Equal(2, set.ForStage(Stage.Inbound).Length);
        Assert.Single(set.ForStage(Stage.Outbound));
        Assert.Empty(set.ForStage(Stage.ToolCall));
    }

    [Fact]
    public void Duplicate_ids_are_rejected_case_insensitively()
    {
        var a = TestPolicies.Injection();
        var b = TestPolicies.Injection() with { Id = "PROMPT_INJECTION" };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([a, b]));
        Assert.Contains(ex.Errors, e => e.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Thresholds_must_ascend()
    {
        var bad = TestPolicies.Injection() with { Thresholds = new ActionThresholds(Flag: 0.9, Block: 0.5) };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([bad]));
        Assert.Contains(ex.Errors, e => e.Contains("ascending", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Choice_actions_must_reference_real_options()
    {
        var bad = TestPolicies.RequestClass() with
        {
            ChoiceRules = new Dictionary<string, ChoiceRule> { ["nope"] = new(VerdictAction.Block) },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([bad]));
        Assert.Contains(ex.Errors, e => e.Contains("unknown option 'nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_document_without_on_error_fails_with_a_pointed_message()
    {
        const string json = """
        {
          "policies": [
            { "id": "x", "stage": "inbound", "type": "noul", "instructions": "Is it spam?", "thresholds": { "block": 0.9 } }
          ]
        }
        """;

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromJson(json));
        Assert.Contains(ex.Errors, e => e.Contains("on_error", StringComparison.Ordinal) && e.Contains("no default", StringComparison.Ordinal));
    }

    [Fact]
    public void FromFile_reads_and_validates_the_document()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{ "policies": [ { "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" } ] }""");

            var set = PolicySet.FromFile(path);

            Assert.Equal("n", Assert.Single(set.All).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_sample_policy_file_loads()
    {
        // tests/Kassad.Tests/bin/<Configuration>/<tfm>/ → repo root; the sample file is the reference policy document.
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Kassad.Sample.ChatApi", "kassad.policies.json"));

        var set = PolicySet.FromFile(path);

        Assert.Equal(8, set.All.Length);
        Assert.Equal(2, set.ForStage(Stage.Inbound).Length);
        Assert.Equal(2, set.ForStage(Stage.Outbound).Length);
        Assert.Equal(2, set.ForStage(Stage.ToolCall).Length);
        Assert.Equal(2, set.ForStage(Stage.Grounding).Length);
    }

    [Fact]
    public void Null_and_blank_inputs_are_rejected_up_front()
    {
        Assert.Throws<ArgumentNullException>(() => PolicySet.FromPolicies(null!));
        Assert.Throws<ArgumentNullException>(() => PolicySet.FromJson(null!));
        Assert.Throws<ArgumentException>(() => PolicySet.FromFile(" "));
    }

    [Fact]
    public void Empty_policy_set_is_rejected()
    {
        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([]));

        Assert.Equal("Policy set contains no policies.", Assert.Single(ex.Errors));
    }

    [Theory]
    [InlineData((Stage)7, ErrorPolicy.FailClosed, "unknown stage '7'")]
    [InlineData(Stage.Inbound, (ErrorPolicy)9, "on_error must be fail_open or fail_closed")]
    public void Undefined_enum_values_are_rejected(Stage stage, ErrorPolicy onError, string expected)
    {
        var bad = TestPolicies.Injection() with { Stage = stage, OnError = onError };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([bad]));

        Assert.Contains(ex.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_question_types_are_rejected()
    {
        var bad = TestPolicies.Injection() with { Question = new UnknownQuestion("q"), Thresholds = null };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromPolicies([bad]));

        Assert.Contains(ex.Errors, e => e.Contains("unsupported question type UnknownQuestion", StringComparison.Ordinal));
    }

    [Fact]
    public void Json_document_round_trips_all_three_question_types()
    {
        const string json = """
        {
          "policies": [
            { "id": "a", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": { "true": "yes means", "false": "no means" },
              "thresholds": { "flag": 0.4, "review": 0.6, "block": 0.85 }, "on_error": "fail_closed" },
            { "id": "b", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "x": "desc", "y": null },
              "actions": { "y": { "action": "block", "min_confidence": 0.7 } }, "on_error": "fail_open" },
            { "id": "c", "stage": "tool_call", "type": "score", "instructions": "q", "criteria": ["low", "mid", "high"],
              "thresholds": { "review": 1, "block": 2 }, "min_confidence": 0.5, "on_error": "fail_closed" }
          ]
        }
        """;

        var set = PolicySet.FromJson(json);

        Assert.Equal(3, set.All.Length);
        var a = Assert.IsType<NoulQuestion>(set.All[0].Question);
        Assert.Equal("yes means", a.Criteria?.True);
        var b = Assert.IsType<ChoiceQuestion>(set.All[1].Question);
        Assert.Null(b.Options["y"]);
        Assert.Equal(VerdictAction.Block, set.All[1].ChoiceRules!["y"].Action);
        Assert.Equal(Stage.ToolCall, set.All[2].Stage);
        Assert.Equal(0.5, set.All[2].MinConfidence);
    }

    /// <summary>A question type the validator has never heard of; exercises its defensive default branch.</summary>
    private sealed record UnknownQuestion(string Instructions) : Question(Instructions);
}

using Kassad.Policies;

namespace Kassad.Tests;

/// <summary>
/// One test per validation rule in <c>Docs/specs/policy-file-format.md</c>, plus the parser leniencies it promises
/// (comments, trailing commas, case-insensitive property names). Every case goes through <see cref="PolicySet.FromJson"/>,
/// the public entry point, so both the document pass and the consistency pass are exercised. The <c>// Rule:</c>
/// comments quote the spec's Validation bullets so the mapping can be checked by reading this file top to bottom.
/// </summary>
public class PolicyDocumentTests
{
    // Rule: file is valid JSON with a top-level `policies` array

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("{}", "'policies'")]
    [InlineData("null", "'policies'")]
    [InlineData("""{ "policies": null }""", "'policies'")]
    [InlineData("""{ "policies": {} }""", "policies")]
    [InlineData("""{ "policies": [ "not an object" ] }""", "policies")]
    public void File_must_be_valid_json_with_a_top_level_policies_array(string document, string expected) =>
        AssertRejected(document, expected);

    [Fact]
    public void Policies_array_must_have_at_least_one_entry() =>
        AssertRejected("""{ "policies": [] }""", "contains no policies");

    // Rule: `id`, `stage`, `type`, `instructions`, `on_error` present

    [Theory]
    [InlineData("""{ "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "empty id")]
    [InlineData("""{ "id": "", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "empty id")]
    [InlineData("""{ "id": "n", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "'stage' is required")]
    [InlineData("""{ "id": "n", "stage": "inbound", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "'type' is required")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "'instructions' is required")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "   ", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "'instructions' is required")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 } }""", "'on_error' is required")]
    public void Required_fields_are_reported_by_name(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Theory]
    [InlineData("""{ "id": "n", "stage": "nowhere", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "stage")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "yesno", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "unknown type 'yesno'")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "maybe" }""", "on_error")]
    public void Enumerated_fields_must_use_a_listed_value(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Fact]
    public void Entries_without_an_id_are_labelled_by_position()
    {
        var document = Document("""{ "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""");

        AssertRejected(document, "policies[0]: 'stage' is required");
    }

    // Rule: `id` unique

    [Theory]
    [InlineData("dup", "dup")]
    [InlineData("dup", "DUP")]
    public void Ids_must_be_unique_case_insensitively(string first, string second)
    {
        var document = Document(NoulWithId(first), NoulWithId(second));

        AssertRejected(document, "duplicate policy id (ids are case-insensitive)");
    }

    // Rule: `criteria` matches the type's shape

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": [], "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "noul 'criteria' must be an object")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": "yes or no", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "noul 'criteria' must be an object")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": 1, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "noul 'criteria' must be an object")]
    public void Noul_criteria_must_be_an_object_of_true_and_false_descriptions(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", null, null)]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": null, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", null, null)]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": {}, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", null, null)]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": { "true": "yes means" }, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "yes means", null)]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": { "false": "no means" }, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", null, "no means")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "criteria": { "true": "t", "false": "f" }, "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""", "t", "f")]
    public void Noul_criteria_are_optional_and_either_key_may_be_omitted(string policy, string? expectedTrue, string? expectedFalse)
    {
        var policyObject = Assert.Single(PolicySet.FromJson(Document(policy)).All);

        var question = Assert.IsType<NoulQuestion>(policyObject.Question);
        Assert.Equal(expectedTrue, question.Criteria?.True);
        Assert.Equal(expectedFalse, question.Criteria?.False);
    }

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""", "choice 'criteria' must be an object")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": ["a", "b"], "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""", "choice 'criteria' must be an object")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": "a", "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""", "choice 'criteria' must be an object")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": {}, "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""", "choice 'criteria' needs at least two options")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "only one" }, "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""", "choice 'criteria' needs at least two options")]
    public void Choice_criteria_must_be_an_object_of_at_least_two_options(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Fact]
    public void Choice_option_descriptions_may_be_null()
    {
        var document = Document("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "described", "b": null }, "actions": { "b": { "action": "block" } }, "on_error": "fail_open" }""");

        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(PolicySet.FromJson(document).All).Question);

        Assert.Equal(2, question.Options.Count);
        Assert.Equal("described", question.Options["a"]);
        Assert.Null(question.Options["b"]);
    }

    [Theory]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "thresholds": { "block": 1 }, "on_error": "fail_closed" }""", "score 'criteria' must be an ordered array")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": { "low": 0 }, "thresholds": { "block": 1 }, "on_error": "fail_closed" }""", "score 'criteria' must be an ordered array")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": "low", "thresholds": { "block": 1 }, "on_error": "fail_closed" }""", "score 'criteria' must be an ordered array")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": [], "thresholds": { "block": 1 }, "on_error": "fail_closed" }""", "score 'criteria' needs at least two levels")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["only"], "thresholds": { "block": 1 }, "on_error": "fail_closed" }""", "score 'criteria' needs at least two levels")]
    public void Score_criteria_must_be_an_array_of_at_least_two_strings(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Fact]
    public void Score_levels_keep_their_order()
    {
        var document = Document("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "mid", "high"], "thresholds": { "block": 2 }, "on_error": "fail_closed" }""");

        var question = Assert.IsType<ScoreQuestion>(Assert.Single(PolicySet.FromJson(document).All).Question);

        Assert.Equal(["low", "mid", "high"], question.Levels);
    }

    // Rule: `thresholds` present with ≥ 1 key, in range, ascending — for noul/score; absent for choice

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": null, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": {}, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "high"], "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "high"], "thresholds": {}, "on_error": "fail_closed" }""")]
    public void Thresholds_are_required_for_noul_and_score_with_at_least_one_key(string policy) =>
        AssertRejected(Document(policy), "'thresholds' must set at least one of flag, review, block");

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "flag": 1.5 }, "on_error": "fail_closed" }""", "threshold 'flag' =", "outside the valid probability range")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": -0.1 }, "on_error": "fail_closed" }""", "threshold 'block' =", "outside the valid probability range")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "mid", "high"], "thresholds": { "review": 2.5 }, "on_error": "fail_closed" }""", "threshold 'review' =", "outside the valid level index range")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "mid", "high"], "thresholds": { "flag": -1 }, "on_error": "fail_closed" }""", "threshold 'flag' =", "outside the valid level index range")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "flag": "high" }, "on_error": "fail_closed" }""", "thresholds.flag", "not valid JSON")]
    public void Thresholds_must_be_numbers_in_range_for_the_type(string policy, string expected, string alsoExpected) =>
        AssertRejected(Document(policy), expected, alsoExpected);

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "flag": 0.6, "review": 0.4 }, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "review": 0.5, "block": 0.5 }, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "flag": 0.9, "block": 0.5 }, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "mid", "high"], "thresholds": { "flag": 2, "review": 1 }, "on_error": "fail_closed" }""")]
    public void Thresholds_must_be_strictly_ascending(string policy) =>
        AssertRejected(Document(policy), "thresholds must be strictly ascending (flag < review < block)");

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block" } }, "thresholds": { "block": 0.9 }, "on_error": "fail_open" }""")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block" } }, "thresholds": {}, "on_error": "fail_open" }""")]
    public void Thresholds_are_forbidden_on_choice(string policy) =>
        AssertRejected(Document(policy), "'thresholds' is only valid for noul and score policies");

    [Theory]
    [InlineData("""{ "flag": 0.4 }""", 0.4, null, null)]
    [InlineData("""{ "review": 0.6 }""", null, 0.6, null)]
    [InlineData("""{ "block": 0.85 }""", null, null, 0.85)]
    [InlineData("""{ "flag": 0.1, "block": 0.9 }""", 0.1, null, 0.9)]
    [InlineData("""{ "flag": 0.0, "review": 0.5, "block": 1.0 }""", 0.0, 0.5, 1.0)]
    public void Any_subset_of_threshold_keys_is_accepted(string thresholds, double? flag, double? review, double? block)
    {
        var document = Document($$"""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": {{thresholds}}, "on_error": "fail_closed" }""");

        var actual = Assert.Single(PolicySet.FromJson(document).All).Thresholds;

        Assert.Equal(new ActionThresholds(flag, review, block), actual);
    }

    // Rule: `actions` present with ≥ 1 entry, each naming a real option — for choice; absent for noul/score

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "on_error": "fail_open" }""")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": null, "on_error": "fail_open" }""")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": {}, "on_error": "fail_open" }""")]
    public void Actions_are_required_for_choice_with_at_least_one_entry(string policy) =>
        AssertRejected(Document(policy), "choice policies need at least one entry in 'actions'");

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "nope": { "action": "block" } }, "on_error": "fail_open" }""", "action refers to unknown option 'nope'")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block" }, "zzz": { "action": "flag" } }, "on_error": "fail_open" }""", "action refers to unknown option 'zzz'")]
    public void Actions_must_name_a_real_option(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "actions": { "x": { "action": "block" } }, "on_error": "fail_closed" }""")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "high"], "thresholds": { "block": 1 }, "actions": { "x": { "action": "block" } }, "on_error": "fail_closed" }""")]
    public void Actions_are_forbidden_on_noul_and_score(string policy) =>
        AssertRejected(Document(policy), "'actions' is only valid for choice policies");

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "x": "1", "y": "2" }, "actions": { "x": {} }, "on_error": "fail_open" }""")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "x": "1", "y": "2" }, "actions": { "x": null }, "on_error": "fail_open" }""")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "x": "1", "y": "2" }, "actions": { "x": { "min_confidence": 0.5 } }, "on_error": "fail_open" }""")]
    public void Each_action_entry_must_specify_its_action(string policy) =>
        AssertRejected(Document(policy), "action for option 'x' must specify 'action'");

    [Fact]
    public void Action_must_be_allow_flag_review_or_block()
    {
        var document = Document("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "explode" } }, "on_error": "fail_open" }""");

        AssertRejected(document, "actions.b.action");
    }

    // Rule: `min_confidence` and each `actions[*].min_confidence` in [0, 1]

    [Theory]
    [InlineData("""{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "min_confidence": 1.5, "on_error": "fail_closed" }""", "n: min_confidence must be between 0 and 1")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block" } }, "min_confidence": -0.1, "on_error": "fail_open" }""", "c: min_confidence must be between 0 and 1")]
    [InlineData("""{ "id": "s", "stage": "outbound", "type": "score", "instructions": "q", "criteria": ["low", "high"], "thresholds": { "block": 1 }, "min_confidence": 2, "on_error": "fail_closed" }""", "s: min_confidence must be between 0 and 1")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block", "min_confidence": 1.01 } }, "on_error": "fail_open" }""", "action 'b' min_confidence must be between 0 and 1")]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block", "min_confidence": -1 } }, "on_error": "fail_open" }""", "action 'b' min_confidence must be between 0 and 1")]
    public void Min_confidence_must_be_between_0_and_1(string policy, string expected) =>
        AssertRejected(Document(policy), expected);

    [Theory]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block", "min_confidence": 1 } }, "min_confidence": 0, "on_error": "fail_open" }""", 0.0, 1.0)]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block", "min_confidence": 0 } }, "min_confidence": 1, "on_error": "fail_open" }""", 1.0, 0.0)]
    [InlineData("""{ "id": "c", "stage": "inbound", "type": "choice", "instructions": "q", "criteria": { "a": "1", "b": "2" }, "actions": { "b": { "action": "block" } }, "on_error": "fail_open" }""", 0.0, 0.0)]
    public void Min_confidence_bounds_are_inclusive_and_default_to_zero(string policy, double expectedPolicy, double expectedRule)
    {
        var policyObject = Assert.Single(PolicySet.FromJson(Document(policy)).All);

        Assert.Equal(expectedPolicy, policyObject.MinConfidence);
        Assert.Equal(expectedRule, policyObject.ChoiceRules!["b"].MinConfidence);
    }

    // Spec promise: every problem is reported, per pass, in one exception

    [Fact]
    public void Document_problems_across_entries_are_reported_together()
    {
        var document = Document(
            """{ "id": "first", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""",
            """{ "id": "second", "stage": "inbound", "type": "choice", "instructions": "q", "actions": { "a": { "action": "block" } }, "on_error": "fail_open" }""");

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromJson(document));

        Assert.Equal(2, ex.Errors.Count);
        Assert.StartsWith("first: 'stage' is required", ex.Errors[0], StringComparison.Ordinal);
        Assert.StartsWith("second: choice 'criteria' must be an object", ex.Errors[1], StringComparison.Ordinal);
        Assert.Contains("2 error(s)", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ex.Errors[0], ex.Message, StringComparison.Ordinal);
        Assert.Contains(ex.Errors[1], ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Consistency_problems_across_entries_are_reported_together()
    {
        // Every entry reads cleanly, so the second pass sees all three problems at once.
        var document = Document(
            """{ "id": "n", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""",
            """{ "id": "N", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 1.5 }, "on_error": "fail_closed" }""",
            """{ "id": "other", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "actions": { "x": { "action": "block" } }, "on_error": "fail_closed" }""");

        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromJson(document));

        Assert.Equal(3, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e.StartsWith("N: duplicate policy id", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.StartsWith("N: threshold 'block' =", StringComparison.Ordinal));
        Assert.Contains(ex.Errors, e => e.StartsWith("other: 'actions' is only valid for choice", StringComparison.Ordinal));
    }

    // Leniency: comments (`//`, `/* */`) and trailing commas are tolerated; property names are case-insensitive on read

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        const string document = """
        {
          // The $schema key is for editors and is ignored by the loader.
          "$schema": "../../schemas/kassad-policies.schema.json",
          "policies": [
            /* block comments work too */
            {
              "id": "n",
              "stage": "inbound",
              "type": "noul",
              "description": "for humans",
              "instructions": "q",
              "thresholds": { "flag": 0.4, "block": 0.9, },
              "on_error": "fail_closed",
            },
          ],
        }
        """;

        var policy = Assert.Single(PolicySet.FromJson(document).All);

        Assert.Equal("n", policy.Id);
        Assert.Equal("for humans", policy.Description);
        Assert.Equal(new ActionThresholds(Flag: 0.4, Block: 0.9), policy.Thresholds);
    }

    [Fact]
    public void Property_names_are_case_insensitive()
    {
        const string document = """
        {
          "POLICIES": [
            {
              "ID": "c",
              "Stage": "inbound",
              "TYPE": "choice",
              "Instructions": "q",
              "Description": "d",
              "CRITERIA": { "a": "1", "b": "2" },
              "Actions": { "b": { "ACTION": "block", "Min_Confidence": 0.7 } },
              "MIN_CONFIDENCE": 0.3,
              "On_Error": "fail_open"
            }
          ]
        }
        """;

        var policy = Assert.Single(PolicySet.FromJson(document).All);

        Assert.Equal("c", policy.Id);
        Assert.Equal(Stage.Inbound, policy.Stage);
        Assert.Equal("q", policy.Question.Instructions);
        Assert.Equal("d", policy.Description);
        Assert.Equal(0.3, policy.MinConfidence);
        Assert.Equal(ErrorPolicy.FailOpen, policy.OnError);
        Assert.Equal(new ChoiceRule(VerdictAction.Block, MinConfidence: 0.7), policy.ChoiceRules!["b"]);
    }

    private static string Document(params string[] policies) =>
        $$"""{ "policies": [ {{string.Join(", ", policies)}} ] }""";

    private static string NoulWithId(string id) =>
        $$"""{ "id": "{{id}}", "stage": "inbound", "type": "noul", "instructions": "q", "thresholds": { "block": 0.9 }, "on_error": "fail_closed" }""";

    /// <summary>Loading must fail with <see cref="PolicyValidationException"/> (exactly that type) and the errors must mention every fragment.</summary>
    private static void AssertRejected(string document, params string[] expectedFragments)
    {
        var ex = Assert.Throws<PolicyValidationException>(() => PolicySet.FromJson(document));

        var errors = string.Join(Environment.NewLine, ex.Errors);
        foreach (var fragment in expectedFragments)
        {
            Assert.Contains(fragment, errors, StringComparison.Ordinal);
        }
    }
}

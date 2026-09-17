using System.Diagnostics;
using System.Diagnostics.Metrics;
using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.Options;

namespace Kassad.Tests;

/// <summary>
/// Pins the surface <c>Docs/specs/telemetry.md</c> documents: one <c>Kassad.Evaluate</c> activity per evaluation with
/// its four tags, one <c>kassad.verdicts</c> increment per verdict, one <c>kassad.model.latency</c> sample per model
/// call. <see cref="ActivityListener"/> and <see cref="MeterListener"/> are process-wide and this assembly runs its
/// test classes in parallel, so every test starts a parent activity of its own and keeps only what the engine emitted
/// underneath it.
/// </summary>
public class GuardrailEngineTelemetryTests
{
    private static PolicySet InboundSet() => PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.RequestClass()]);

    private static FakeDecisionModel Benign(FakeDecisionModel? model = null) => (model ?? new FakeDecisionModel())
        .Answer("prompt_injection", new NoulAnswer(0.05))
        .Answer("request_class", new ChoiceAnswer("support", new Dictionary<string, double> { ["support"] = 0.9, ["prohibited"] = 0.1 }, 0.9));

    [Fact]
    public void Names_match_the_spec()
    {
        Assert.Equal("Kassad", KassadTelemetry.ActivitySourceName);
        Assert.Equal("Kassad", KassadTelemetry.MeterName);
        Assert.Equal("Kassad.Evaluate", KassadTelemetry.EvaluationActivityName);
        Assert.Equal("kassad.verdicts", KassadTelemetry.VerdictsInstrumentName);
        Assert.Equal("kassad.model.latency", KassadTelemetry.ModelLatencyInstrumentName);
    }

    [Fact]
    public async Task Each_evaluation_emits_one_activity_with_the_four_tags()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(Benign(), InboundSet());

        await engine.EvaluateAsync(Stage.Inbound, "hello");

        var activity = Assert.Single(telemetry.Activities);
        Assert.Equal("Kassad", activity.Source.Name);
        Assert.Equal("Kassad.Evaluate", activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal("inbound", activity.GetTagItem("kassad.stage"));
        Assert.Equal("allow", activity.GetTagItem("kassad.outcome"));
        Assert.Equal("fake", activity.GetTagItem("kassad.model"));
        Assert.False(Assert.IsType<bool>(activity.GetTagItem("kassad.had_error")));
        Assert.Equal(4, activity.TagObjects.Count());
    }

    [Fact]
    public async Task Each_verdict_increments_the_counter_once_with_its_tags()
    {
        using var telemetry = new TelemetryCapture();
        var model = new FakeDecisionModel()
            .Answer("prompt_injection", new NoulAnswer(0.05))
            .Answer("request_class", new ChoiceAnswer("prohibited", new Dictionary<string, double> { ["support"] = 0.1, ["prohibited"] = 0.9 }, 0.95));
        var engine = new GuardrailEngine(model, InboundSet());

        await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.Equal(2, telemetry.VerdictIncrements.Count);
        Assert.All(telemetry.VerdictIncrements, m => Assert.Equal(1, m.Value));
        Assert.All(telemetry.VerdictIncrements, m => Assert.Equal(4, m.Tags.Count));
        Assert.All(telemetry.VerdictIncrements, m => Assert.Equal("inbound", m.Tags["kassad.stage"]));
        Assert.All(telemetry.VerdictIncrements, m => Assert.False(Assert.IsType<bool>(m.Tags["kassad.from_error"])));
        Assert.Equal("allow", telemetry.ForPolicy("prompt_injection").Tags["kassad.action"]);
        Assert.Equal("block", telemetry.ForPolicy("request_class").Tags["kassad.action"]);
    }

    [Fact]
    public async Task Three_evaluations_of_two_policies_give_three_activities_six_increments_and_three_latency_samples()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(Benign(), InboundSet());

        for (var i = 0; i < 3; i++)
        {
            await engine.EvaluateAsync(Stage.Inbound, $"message {i}");
        }

        Assert.Equal(3, telemetry.Activities.Count);
        Assert.Equal(6, telemetry.VerdictIncrements.Count);
        Assert.Equal(3, telemetry.LatencySamples.Count);
    }

    [Fact]
    public async Task Model_latency_is_the_result_latency_in_milliseconds_recorded_inside_the_activity()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(Benign(new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(20) }), InboundSet());

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        var sample = Assert.Single(telemetry.LatencySamples);
        Assert.Equal(result.ModelLatency.TotalMilliseconds, sample.Value);
        Assert.True(sample.Value > 0);
        Assert.Equal(3, sample.Tags.Count);
        Assert.Equal("inbound", sample.Tags["kassad.stage"]);
        Assert.Equal("fake", sample.Tags["kassad.model"]);
        Assert.False(Assert.IsType<bool>(sample.Tags["kassad.had_error"]));

        // Measurements are taken while the evaluation's activity is current, so an exporter that samples exemplars
        // can link a histogram bucket or a counter increment back to the trace.
        var activity = Assert.Single(telemetry.Activities);
        Assert.Same(activity, sample.Current);
        Assert.All(telemetry.VerdictIncrements, m => Assert.Same(activity, m.Current));
    }

    [Fact]
    public async Task Model_failure_is_flagged_on_every_signal_and_the_activity_status_stays_unset()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(new FakeDecisionModel { ThrowOnEvaluate = new DecisionModelException("529") }, InboundSet());

        await engine.EvaluateAsync(Stage.Inbound, "hello");

        var activity = Assert.Single(telemetry.Activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status); // the engine produced verdicts, by design
        Assert.Equal("block", activity.GetTagItem("kassad.outcome")); // prompt_injection is fail_closed
        Assert.True(Assert.IsType<bool>(activity.GetTagItem("kassad.had_error")));

        Assert.Equal(2, telemetry.VerdictIncrements.Count);
        Assert.All(telemetry.VerdictIncrements, m => Assert.True(Assert.IsType<bool>(m.Tags["kassad.from_error"])));
        Assert.Equal("block", telemetry.ForPolicy("prompt_injection").Tags["kassad.action"]);
        Assert.Equal("allow", telemetry.ForPolicy("request_class").Tags["kassad.action"]); // fail_open

        var sample = Assert.Single(telemetry.LatencySamples);
        Assert.True(Assert.IsType<bool>(sample.Tags["kassad.had_error"]));
    }

    [Fact]
    public async Task Budget_overrun_is_an_error_whose_latency_is_the_time_until_the_budget_fired()
    {
        using var telemetry = new TelemetryCapture();
        var model = Benign(new FakeDecisionModel { Delay = TimeSpan.FromMilliseconds(500) });
        var engine = new GuardrailEngine(model, InboundSet(), Options.Create(new EvaluationOptions { Budget = TimeSpan.FromMilliseconds(50) }));

        var result = await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.True(result.BudgetExceeded);
        var activity = Assert.Single(telemetry.Activities);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.True(Assert.IsType<bool>(activity.GetTagItem("kassad.had_error")));
        Assert.All(telemetry.VerdictIncrements, m => Assert.True(Assert.IsType<bool>(m.Tags["kassad.from_error"])));
        var sample = Assert.Single(telemetry.LatencySamples);
        Assert.Equal(result.ModelLatency.TotalMilliseconds, sample.Value);
        Assert.True(Assert.IsType<bool>(sample.Tags["kassad.had_error"]));
    }

    [Fact]
    public async Task Stage_without_policies_gets_an_activity_but_no_measurements()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(new FakeDecisionModel(), InboundSet());

        await engine.EvaluateAsync(Stage.Grounding, "anything");

        var activity = Assert.Single(telemetry.Activities);
        Assert.Equal("grounding", activity.GetTagItem("kassad.stage"));
        Assert.Equal("allow", activity.GetTagItem("kassad.outcome"));
        Assert.Equal("fake", activity.GetTagItem("kassad.model"));
        Assert.False(Assert.IsType<bool>(activity.GetTagItem("kassad.had_error")));
        Assert.Empty(telemetry.VerdictIncrements);
        Assert.Empty(telemetry.LatencySamples);
    }

    [Fact]
    public async Task Caller_cancellation_ends_the_activity_with_an_error_status_and_no_outcome_or_measurements()
    {
        using var telemetry = new TelemetryCapture();
        var engine = new GuardrailEngine(new FakeDecisionModel { ThrowOnEvaluate = new OperationCanceledException() }, InboundSet());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.EvaluateAsync(Stage.Inbound, "x", cts.Token));

        var activity = Assert.Single(telemetry.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("inbound", activity.GetTagItem("kassad.stage"));
        Assert.Equal("fake", activity.GetTagItem("kassad.model"));
        Assert.Null(activity.GetTagItem("kassad.outcome"));
        Assert.Null(activity.GetTagItem("kassad.had_error"));
        Assert.Empty(telemetry.VerdictIncrements);
        Assert.Empty(telemetry.LatencySamples);
    }

    [Theory]
    [InlineData(Stage.Inbound, "inbound")]
    [InlineData(Stage.Outbound, "outbound")]
    [InlineData(Stage.ToolCall, "tool_call")]
    [InlineData(Stage.Grounding, "grounding")]
    public async Task Stage_tags_use_the_policy_file_spelling(Stage stage, string expected)
    {
        using var telemetry = new TelemetryCapture();
        var set = PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.HarmSeverity(), TestPolicies.ToolCallOffIntent(), TestPolicies.ClaimUnsupported()]);
        var model = new FakeDecisionModel()
            .Answer("prompt_injection", new NoulAnswer(0.05))
            .Answer("harm_severity", new ScoreAnswer(0.2, ["none", "minor", "serious", "severe"], [0.8, 0.2, 0, 0], 0.9))
            .Answer("tool_call_off_intent", new NoulAnswer(0.05))
            .Answer("claim_unsupported", new NoulAnswer(0.05));
        var engine = new GuardrailEngine(model, set);

        await engine.EvaluateAsync(stage, "state");

        Assert.Equal(expected, Assert.Single(telemetry.Activities).GetTagItem("kassad.stage"));
        Assert.Equal(expected, Assert.Single(telemetry.VerdictIncrements).Tags["kassad.stage"]);
        Assert.Equal(expected, Assert.Single(telemetry.LatencySamples).Tags["kassad.stage"]);
    }

    [Theory]
    [InlineData(0.05, "allow")]
    [InlineData(0.5, "flag")]
    [InlineData(0.7, "review")]
    [InlineData(0.9, "block")]
    public async Task Outcome_and_action_tags_use_the_lower_case_action_names(double probability, string expected)
    {
        using var telemetry = new TelemetryCapture();
        var model = new FakeDecisionModel().Answer("prompt_injection", new NoulAnswer(probability));
        var engine = new GuardrailEngine(model, PolicySet.FromPolicies([TestPolicies.Injection()]));

        await engine.EvaluateAsync(Stage.Inbound, "hello");

        Assert.Equal(expected, Assert.Single(telemetry.Activities).GetTagItem("kassad.outcome"));
        Assert.Equal(expected, Assert.Single(telemetry.VerdictIncrements).Tags["kassad.action"]);
    }

    /// <summary>One measurement as the listener saw it, with the activity that was current when it was recorded.</summary>
    private sealed record Captured<T>(T Value, Dictionary<string, object?> Tags, Activity Current);

    /// <summary>
    /// Listens to the <c>Kassad</c> source and meter for the lifetime of one test. Everything is attributed through a
    /// parent activity the capture starts: the engine's activity becomes its child, and measurements are kept only
    /// when the activity current at recording time is that child (or the parent itself).
    /// </summary>
    private sealed class TelemetryCapture : IDisposable
    {
        private readonly object _gate = new();
        private readonly Activity _parent;
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters;

        public TelemetryCapture()
        {
            _parent = new Activity(nameof(GuardrailEngineTelemetryTests)).Start();

            _activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name == KassadTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Parent == _parent)
                    {
                        lock (_gate)
                        {
                            Activities.Add(activity);
                        }
                    }
                },
            };
            ActivitySource.AddActivityListener(_activities);

            _meters = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == KassadTelemetry.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Capture(instrument, KassadTelemetry.VerdictsInstrumentName, VerdictIncrements, value, tags));
            _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Capture(instrument, KassadTelemetry.ModelLatencyInstrumentName, LatencySamples, value, tags));
            _meters.Start();
        }

        public List<Activity> Activities { get; } = [];

        public List<Captured<long>> VerdictIncrements { get; } = [];

        public List<Captured<double>> LatencySamples { get; } = [];

        public Captured<long> ForPolicy(string policyId) => VerdictIncrements.Single(m => (string?)m.Tags["kassad.policy_id"] == policyId);

        public void Dispose()
        {
            _meters.Dispose();
            _activities.Dispose();
            _parent.Dispose();
        }

        private void Capture<T>(Instrument instrument, string name, List<Captured<T>> into, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var current = Activity.Current;
            if (instrument.Name != name || current is null || (current != _parent && current.Parent != _parent))
            {
                return;
            }

            var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                into.Add(new Captured<T>(value, copy, current));
            }
        }
    }
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kassad;

/// <summary>
/// The traces and metrics <see cref="Engine.GuardrailEngine"/> emits, and their names. Everything goes through
/// <c>System.Diagnostics</c>: an <see cref="ActivitySource"/> and a <see cref="Meter"/>, both called <c>Kassad</c>, so
/// an OpenTelemetry pipeline picks them up with <c>AddSource(KassadTelemetry.ActivitySourceName)</c> and
/// <c>AddMeter(KassadTelemetry.MeterName)</c>, and <c>dotnet-counters</c> with <c>--counters Kassad</c>. Kassad itself
/// takes no OpenTelemetry dependency. <c>Docs/specs/telemetry.md</c> lists every tag and value.
/// </summary>
public static class KassadTelemetry
{
    /// <summary>Name of the <see cref="ActivitySource"/> that starts one activity per stage evaluation.</summary>
    public const string ActivitySourceName = "Kassad";

    /// <summary>Name of the <see cref="Meter"/> that owns <see cref="VerdictsInstrumentName"/> and <see cref="ModelLatencyInstrumentName"/>.</summary>
    public const string MeterName = "Kassad";

    /// <summary>
    /// Operation name of the activity that spans one <see cref="Engine.IGuardrailEngine.EvaluateAsync"/> call. Tagged
    /// <c>kassad.stage</c> and <c>kassad.model</c> when it starts, <c>kassad.outcome</c> and <c>kassad.had_error</c> when
    /// the evaluation completes.
    /// </summary>
    public const string EvaluationActivityName = "Kassad.Evaluate";

    /// <summary>
    /// Counter with one increment per verdict, tagged <c>kassad.stage</c>, <c>kassad.policy_id</c>, <c>kassad.action</c>
    /// and <c>kassad.from_error</c>.
    /// </summary>
    public const string VerdictsInstrumentName = "kassad.verdicts";

    /// <summary>
    /// Histogram of the decision-model call's wall-clock time in milliseconds, one sample per call, tagged
    /// <c>kassad.stage</c>, <c>kassad.model</c> and <c>kassad.had_error</c>.
    /// </summary>
    public const string ModelLatencyInstrumentName = "kassad.model.latency";

    internal const string StageTag = "kassad.stage";
    internal const string ModelTag = "kassad.model";
    internal const string OutcomeTag = "kassad.outcome";
    internal const string HadErrorTag = "kassad.had_error";
    internal const string PolicyIdTag = "kassad.policy_id";
    internal const string ActionTag = "kassad.action";
    internal const string FromErrorTag = "kassad.from_error";

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter KassadMeter = new(MeterName);

    private static readonly Counter<long> Verdicts = KassadMeter.CreateCounter<long>(
        VerdictsInstrumentName, unit: "{verdict}", description: "Verdicts resolved, one per policy per stage evaluation.");

    private static readonly Histogram<double> ModelLatency = KassadMeter.CreateHistogram<double>(
        ModelLatencyInstrumentName, unit: "ms", description: "Wall-clock time of one decision-model call.");

    // Boxed once, so the bool tags allocate nothing per verdict.
    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    /// <summary>
    /// Start the activity for one evaluation and tag what is known up front. Returns <c>null</c> when nothing listens
    /// to the source, in which case nothing is allocated.
    /// </summary>
    internal static Activity? StartEvaluation(Stage stage, string model)
    {
        var activity = Source.StartActivity(EvaluationActivityName);
        if (activity is not null)
        {
            activity.SetTag(StageTag, StageName(stage));
            activity.SetTag(ModelTag, model);
        }

        return activity;
    }

    /// <summary>
    /// Tag the activity with the result, count its verdicts and, when the model was called, record the call's latency.
    /// Runs while the activity is still current, so a metrics exporter that samples exemplars can link them to it.
    /// </summary>
    internal static void RecordEvaluation(Activity? activity, StageResult result, string model, bool modelCalled)
    {
        var stage = StageName(result.Stage);
        var hadError = Box(result.HadModelError);

        if (activity is not null)
        {
            activity.SetTag(OutcomeTag, ActionName(result.Outcome));
            activity.SetTag(HadErrorTag, hadError);
        }

        if (Verdicts.Enabled)
        {
            foreach (var verdict in result.Verdicts)
            {
                Verdicts.Add(1, new TagList
                {
                    { StageTag, stage },
                    { PolicyIdTag, verdict.PolicyId },
                    { ActionTag, ActionName(verdict.Action) },
                    { FromErrorTag, Box(verdict.FromError) },
                });
            }
        }

        if (modelCalled && ModelLatency.Enabled)
        {
            ModelLatency.Record(result.ModelLatency.TotalMilliseconds, new TagList
            {
                { StageTag, stage },
                { ModelTag, model },
                { HadErrorTag, hadError },
            });
        }
    }

    /// <summary>Mark an evaluation the caller cancelled. It produced no outcome, so only the status says how it ended.</summary>
    internal static void RecordCancellation(Activity? activity) => activity?.SetStatus(ActivityStatusCode.Error, "cancelled by the caller");

    /// <summary>The stage as the policy file spells it.</summary>
    internal static string StageName(Stage stage) => stage switch
    {
        Stage.Inbound => "inbound",
        Stage.Outbound => "outbound",
        Stage.ToolCall => "tool_call",
        Stage.Grounding => "grounding",
        _ => stage.ToString().ToLowerInvariant(),
    };

    /// <summary>The action in lower case, as the <c>Kassad-Outcome</c> header spells it.</summary>
    internal static string ActionName(VerdictAction action) => action switch
    {
        VerdictAction.Allow => "allow",
        VerdictAction.Flag => "flag",
        VerdictAction.Review => "review",
        VerdictAction.Block => "block",
        _ => action.ToString().ToLowerInvariant(),
    };

    private static object Box(bool value) => value ? BoxedTrue : BoxedFalse;
}

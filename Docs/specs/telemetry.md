# Telemetry

Status: implemented (roadmap 3.3). The names below are a contract from `0.1.0` on; until then a rename is a
`CHANGELOG.md` entry, never a silent change.

`GuardrailEngine` reports every stage evaluation through `System.Diagnostics` alone: an `ActivitySource` and a
`Meter`, both named `Kassad`. Kassad takes no OpenTelemetry package dependency, and nothing here does work unless
something listens: `ActivitySource.StartActivity` returns `null` without a listener, and the instruments skip their
work while `Instrument.Enabled` is false. Tags carry no content: no state, no reason text, no probabilities. The
detail behind a verdict stays in the log lines `samples/README.md` shows.

The names live on `Kassad.KassadTelemetry` as constants: `ActivitySourceName`, `MeterName`,
`EvaluationActivityName`, `VerdictsInstrumentName`, `ModelLatencyInstrumentName`.

## Wiring

OpenTelemetry (`OpenTelemetry.Extensions.Hosting`):

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(KassadTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(KassadTelemetry.MeterName));
```

Without a collector, `dotnet-counters` (`dotnet tool install -g dotnet-counters`) shows the meter live:

```bash
dotnet-counters monitor --name Kassad.Sample.ChatApi --counters Kassad
```

`dotnet-monitor` and any `MeterListener` / `ActivityListener` work the same way;
`tests/Kassad.Tests/GuardrailEngineTelemetryTests.cs` is a listener example.

## Trace: `Kassad.Evaluate`

Source `Kassad`, `ActivityKind.Internal`, one activity per `IGuardrailEngine.EvaluateAsync` call on
`GuardrailEngine`, including calls through `EvaluateToolCallAsync` / `EvaluateGroundingAsync`, which forward to it.
It is a child of whatever `Activity.Current` is when the engine is called: under the middleware and the handler
alike that is normally the ASP.NET Core request span (the provider call the handler screens is a sibling `HttpClient`
span). With `HttpClient` instrumentation on, the TypeSafe call appears as a child of the evaluation.

| Tag | Type | Values | Set |
|---|---|---|---|
| `kassad.stage` | string | `inbound`, `outbound`, `tool_call`, `grounding`: the policy-file spelling | at start |
| `kassad.model` | string | `IDecisionModel.Name` verbatim, e.g. `typesafe:jev-latest` | at start |
| `kassad.outcome` | string | `allow`, `flag`, `review`, `block`, as the `Kassad-Outcome` header spells them: `StageResult.Outcome` | on completion |
| `kassad.had_error` | bool | `StageResult.HadModelError`: true when any verdict came from a policy's `on_error` (model failure, missing answer, budget overrun) | on completion |

Status:

- `Unset` for every evaluation that returned a `StageResult`, model failures and budget overruns included. The
  engine produced verdicts, by design; `kassad.had_error` says the model did not answer, and the model call's own
  span carries the transport error.
- `Error`, description `cancelled by the caller`, when the caller's `CancellationToken` cancelled the evaluation
  and `OperationCanceledException` propagated. That activity has `kassad.stage` and `kassad.model` but no
  `kassad.outcome` or `kassad.had_error`.

A stage with no registered policies gets an activity like any other (`kassad.outcome=allow`,
`kassad.had_error=false`, near-zero duration) and no measurements.

The activity's duration covers the whole call: policy lookup, the model call, verdict resolution and logging. The
model call alone is `kassad.model.latency`.

## Metrics: meter `Kassad`

### `kassad.verdicts`: `Counter<long>`, unit `{verdict}`

One increment per verdict, so an evaluation of N policies adds N.

| Tag | Values |
|---|---|
| `kassad.stage` | as above |
| `kassad.policy_id` | the policy `id` from the policy file |
| `kassad.action` | `allow`, `flag`, `review`, `block`: the verdict's action |
| `kassad.from_error` | bool, `Verdict.FromError`: true when the action came from `on_error` rather than an answer |

Cardinality is bounded by the policy file: policies × 4 actions × 2. The block rate of a stage is
`sum(kassad.action=block) / sum(*)` over its policies; the share with `kassad.from_error=true` is the model's
error rate as the policies experienced it.

### `kassad.model.latency`: `Histogram<double>`, unit `ms`

One sample per decision-model call: `StageResult.ModelLatency`, the wall-clock time of `IDecisionModel.EvaluateAsync`
as the engine measured it, including any retries inside the client and, for a budget overrun, the time until the
budget fired (about the budget). Not recorded when the stage has no policies (no call was made) or when the caller
cancelled (that duration would be the caller's, not the model's).

| Tag | Values |
|---|---|
| `kassad.stage` | as above |
| `kassad.model` | as above |
| `kassad.had_error` | as above; filter on `false` for the latency of calls that answered |

Milliseconds rather than the seconds OpenTelemetry's semantic conventions prefer for durations: the OpenTelemetry
SDK's default explicit buckets (0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1 000, 2 500, 5 000, 7 500, 10 000) are
millisecond-shaped, and `InstrumentAdvice` for custom default buckets exists only from .NET 9, so a net8.0 consumer
gets a usable distribution without configuring a view. The unit is in the instrument's metadata, so exporters and
`dotnet-counters` print it.

Measurements are recorded while `Kassad.Evaluate` is `Activity.Current`, so an exporter that samples exemplars can
link a bucket or an increment back to the trace.

## Conventions

- Tag names are `kassad.`-prefixed snake_case and mean the same thing on the activity and on both instruments.
  The roadmap's `stage, policy_id, action, from_error` shorthand is these names.
- Tag values are lower-case snake_case: the policy file's spelling for stages, the `Kassad-Outcome` header's for
  actions.
- Nothing user-provided is a tag: no state, no answers, no reasons. `kassad.policy_id` is operator-controlled.
- The source and the meter are static: one per process, created on first use, never disposed. There is no
  `IMeterFactory` path, so the `GuardrailEngine` constructor did not change for this.

## Not emitted

- Token usage. `StageResult.Usage` carries it per evaluation; a `kassad.model.tokens` counter is a candidate once
  the eval harness needs it.
- Per-policy latency. Every policy for a stage goes out in one request, so latency is per stage by construction.
- Middleware or handler spans. The ASP.NET Core request span and the `HttpClient` span already exist; the
  evaluation activity sits under them.
- Anything from `Kassad.TypeSafe` itself. Its HTTP call is covered by `HttpClient` instrumentation.

## Verified

`GuardrailEngineTelemetryTests` (17 tests) pins everything above with an `ActivityListener` and a `MeterListener`.
On 2026-09-17 the sample ran against the live API with `dotnet-counters collect --counters Kassad` attached for 25 s
while three benign and three injection prompts went through. It recorded four `kassad.verdicts` series (`allow`
and `block` for each of `prompt_injection` and `request_class`, three increments each) and `kassad.model.latency
(ms)` p50/p95/p99 of 520, 261, 283, 214, 150 and 240 ms for the six one-request intervals, all tagged
`kassad.stage=inbound,kassad.model=typesafe:jev-latest,kassad.had_error=False`. `samples/README.md` shows the rows.

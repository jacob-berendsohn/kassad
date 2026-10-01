# Kassad.Abstractions

`IDecisionModel`, the Noul/Choice/Score primitives, `Verdict`. Reference this to implement a model or consume verdicts
without taking a dependency on the Kassad engine. Kassad is calibrated guardrails for LLM applications in .NET: typed
checks answered by a System One decision model (TypeSafe's Jev by default), resolved in your code into `Allow` /
`Flag` / `Review` / `Block` verdicts with the probability and confidence behind them.

> **Status: `0.1.0`, first release.** The public API can still change before `1.0`.

## Install

```bash
dotnet add package Kassad.Abstractions
```

Applications take `Kassad.AspNetCore`, `Kassad` or `Kassad.TypeSafe` instead; each brings this package in.

## What is here

- `IDecisionModel`: `Name` and `EvaluateAsync(DecisionRequest, CancellationToken)`. Implement it to back Kassad with
  another provider, or with a fake in tests; `Kassad.TypeSafe` is the implementation for TypeSafe's `POST /v1/systemone`.
- `DecisionRequest` (a state object plus named `Question`s) and `DecisionResponse` (model name, named `Answer`s,
  `TokenUsage`); `DecisionModelException` is what an implementation throws when the provider cannot answer.
- `NoulQuestion` / `NoulAnswer` (a probability of "yes"), `ChoiceQuestion` / `ChoiceAnswer` (the chosen option, the
  distribution over options, a confidence), `ScoreQuestion` / `ScoreAnswer` (a score over ordered levels, the
  distribution over them, a confidence).
- `Verdict` (policy id, stage, action, value, confidence, reason, the answer, `FromError`) and `StageResult` (stage,
  outcome, verdicts, `HadModelError`, `BudgetExceeded`, model latency, usage); `VerdictAction` is ordered
  `Allow < Flag < Review < Block` and a stage's outcome is its highest verdict.
- `Stage` (`Inbound`, `Outbound`, `ToolCall`, `Grounding`) and the typed states for the two code-only stages,
  `ToolCallState(UserIntent, ToolName, ToolSchemaJson, ArgumentsJson)` and `GroundingState(Claim, SourcePassage, SourceId)`.
- `ErrorPolicy` (`FailOpen` / `FailClosed`), each policy's mandatory answer to a model failure.

The abstractions are serialization-free: no JSON attributes, no dependencies. Wire mapping belongs to the model
implementation.

## Policies and numbers

The engine, the policy file format, the stages and the **Numbers** section (generated from committed evaluation runs,
with what those numbers do and do not mean) are in the
[repository README](https://github.com/jacob-berendsohn/kassad#readme).

Apache-2.0.

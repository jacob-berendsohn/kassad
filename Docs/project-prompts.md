# Project Prompts — Kassad 0.1.0

| | |
|---|---|
| **Prompts generated** | `2026-09-17 17:17` (fourth in-place refresh of the `2026-09-17 12:27` generation; see the refresh note below) |
| **Roadmap last modified** | `2026-09-17` per the most recent `Docs/roadmap.md` changelog entry, the 1.2 end state (the changelog records dates only; the file was last committed `2026-09-17 17:16`, `0adea7b`) |

> **Staleness check.** If the roadmap's last-modified timestamp is newer than the prompts-generated timestamp, **this file is stale**. Regenerate it before executing any prompt; the prompts below may not reflect the current roadmap. Do not edit prompts here in place — fix the roadmap and regenerate.

> **Refreshed in place 2026-09-17 17:17** (earlier refreshes 2026-09-17 14:47, 15:34 and 16:43). Between generation (`6892f84`) and `0adea7b` the roadmap changed only in two Prerequisites bullets (owner placeholder, then the nuget.org policy, each recorded as done) and its changelog (0.1–0.5, 1.1 and 1.2 end states); the step from `9a48f5f` to `0adea7b` is the single 1.2 changelog line. No sub-phase Goal, Deliverables, Verification or Depends-on line changed, and a script re-checked every prompt below verbatim against the roadmap (21 sub-phases, 0 differences). Header timestamps and the done markers on Prompts 0.1–0.5, 1.1 and 1.2 are the only edits since generation. The next roadmap change that touches a sub-phase field needs a full regeneration, not another refresh.

## How to use

1. **Check the staleness banner above.** If the roadmap is newer than this file, stop and regenerate.
2. Run prompts in order. Each prompt is one session against a coding agent.
3. **Set the session to the prompt's recommended effort before pasting it** — launch with `--effort <level>` or run `/effort <level>` first. The recommendation is a default, not a mandate; raise it if the session starts fighting you, and note persistent mismatches for the next groom.
4. Before starting a prompt, verify the previous sub-phase's verification step actually passed.
5. After each session, update the roadmap's changelog if the phase or sub-phase reached its end state.
6. If a prompt produces work that materially changes `PROJECT_CONTEXT.md`, update it in the same session.

> **Verification runs inline.** No `verifier` subagent was found at `.claude/agents/verifier.md` or `~/.claude/agents/verifier.md` when this file was generated, so every Verification section below runs in the main session, which is significantly more expensive on a premium model. Add a verifier subagent and regenerate to move build, test, and harness output out of the main session's context.

## Out of scope (applies to every prompt)

- A standalone OpenAI-compatible reverse proxy (`kassad-proxy`). Separate initiative after 0.1.
- A Python port or any non-.NET SDK.
- An LLM-backed fallback `IDecisionModel` (`Kassad.LlmJudge`). Design reference noted in `Docs/research/typesafe-api-notes.md`; not built here.
- YAML policy files or any second policy format.
- Token-level evaluation of streaming responses. 3.4 produces a design note only.
- Composite policies (`all_of` / `any_of`), per-policy model selection, threshold expressions.
- A dashboard, UI, or hosted service of any kind.
- Reserved-prefix application for `Kassad.*` on nuget.org (requires published packages first; do after 0.1.0).
- Eval datasets beyond inbound + one grounding set. Outbound/tool-call datasets are a 0.2 concern.

## Prompts

Phases 0 → 1 → 2 are strictly sequential. Phase 3 and Phase 4 depend on Phase 1 and can run in parallel with Phase 2 and with each other, except 4.4 which depends on everything. Parallel opportunities are flagged on the individual prompts.

### Phase 0 — Scaffold builds, client verified, first preview on nuget.org

#### Prompt 0.1 — Scaffold compiles warning-free

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `medium` — mechanical maintenance with a safety net: the blockers that needed judgment are already fixed; what remains is a placeholder sweep and version bumps proven by a green build after each group and by the CI run.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 0, Sub-phase 0.1)

**Goal.** Get the hand-written scaffold to build with zero warnings on the pinned SDK without lowering any standard.

**Deliverables.** `__GITHUB_OWNER__` replaced everywhere; `Authors` confirmed; package versions in `Directory.Packages.props` bumped to current with a build after each group (latest as of 2026-09-17: MinVer 8.0.0, Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0, Microsoft.NET.Test.Sdk 18.10.1, xunit.runner.visualstudio 4.0.0, coverlet.collector 10.0.1, Microsoft.Extensions.* 10.0.12 for the non-net8.0 group; confirm xunit.runner.visualstudio 4.x still runs xunit v2 tests before taking it); anything CI reports that the local build did not.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** `dotnet build -c Release` → 0 errors and no warnings other than RS0016/RS0017, locally and in CI on both OSes. `grep -r __GITHUB_OWNER__ .` returns nothing outside `obj/`. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** None.

#### Prompt 0.2 — Unit tests green in CI

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `medium` — the tests already pass locally on both TFMs; the remaining work is CI YAML plumbing (trx logger, Live filter) verified by the CI run itself.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 0, Sub-phase 0.2)

**Goal.** Every test in `tests/` passes on both CI platforms with no network.

**Deliverables.** Fixes to test or product code as needed; `ci.yml` uploading `.trx` results (switch the logger to `LogFilePrefix`; the fixed `LogFileName=results.trx` makes the net8.0 and net10.0 runs of each test project overwrite each other); a `[Trait("Category","Live")]` convention documented in `CONTRIBUTING.md` (already stated) and enforced by a `--filter Category!=Live` in the `build` job.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** `dotnet test -c Release --filter "Category!=Live"` green locally and in CI; the `build` job shows test counts > 0 for both test projects. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.1.

#### Prompt 0.3 — Wire format verified against live responses

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `high` — first live integration: recording real responses, adding `Category=Live` tests, and reconciling the API notes against whatever the wire actually returns is discovery work.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 0, Sub-phase 0.3)
- `Docs/research/typesafe-api-notes.md`

**Goal.** Replace transcribed fixtures with recorded ones and prove the client round-trips all three primitives against the real API.

**Deliverables.** A small recorder (test helper or `eval` subcommand) that hits `/v1/systemone` with the fixture questions and writes the raw response to `Fixtures/` with a `recorded_at` field; updated `SystemOneWireTests` reading those files; `TypeSafeLiveTests` (`Category=Live`) covering one request per primitive, a 422 on a bad body, and that `usage` is populated; `Docs/research/typesafe-api-notes.md` corrected for any divergence found.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** `TYPESAFE_API_KEY=... dotnet test --filter Category=Live` green; fixtures diff shows real `model` string and token counts; the `live` CI job passes. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.2; TypeSafe API key.

#### Prompt 0.4 — Public API baselined and analysis level raised

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `high` — API-freeze work: deciding what stays public and triaging every diagnostic that `latest-recommended` raises requires judgment the roadmap could not pre-specify.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 0, Sub-phase 0.4)

**Goal.** Lock the public surface so unreviewed changes fail the build, and move to the recommended analyzer set.

**Deliverables.** `PublicAPI.Unshipped.txt` populated in all four `src/` projects (use the analyzer's code fix, then review by hand and remove anything that shouldn't be public); `WarningsNotAsErrors` for `RS00xx` removed from `src/Directory.Build.props`; `AnalysisLevel` set to `latest-recommended` in the root props and every new diagnostic either fixed or explicitly suppressed in `.editorconfig` with a one-line reason.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Build clean; adding a throwaway public method without touching the API file fails the build with `RS0016`; `.editorconfig` contains no bare `severity = none` without a comment. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.3 (so the surface being baselined is the one that survived live testing).

#### Prompt 0.5 — First preview published by tag

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `high` — crosses the GitHub ↔ nuget.org seam for the first time (OIDC Trusted Publishing, environments, secrets); the release path proven here is the one 4.5 reuses.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 0, Sub-phase 0.5)

**Goal.** Prove the release path end-to-end with a pre-release version.

**Deliverables.** nuget.org Trusted Publishing policy + GitHub `nuget` environment + `NUGET_USER` secret configured; `git tag v0.1.0-preview.1` pushed; the `Release` workflow run green; a GitHub release with attached `.nupkg`/`.snupkg`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** Do not apply for the `Kassad.*` reserved prefix on nuget.org; that happens after 0.1.0.

**Verification.** `dotnet add package Kassad.TypeSafe --prerelease` in a scratch console project restores and `new TypeSafeClient(...)` compiles; nuget.org package pages show README + license + both TFMs + SourceLink badge. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.4; nuget.org account and policy.

### Phase 1 — Engine verified end-to-end

#### Prompt 1.1 — Policy loader covered rule-for-rule

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

**Session setup.** Effort: `high` — reconciles the written spec against the loader rule by rule, which may surface rules missing from the code, and fixes a control-flow bug in `PolicyDocument.Parse`.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 1, Sub-phase 1.1)
- `Docs/specs/policy-file-format.md`

**Goal.** One test per validation rule in the spec, plus parser leniency (comments, trailing commas, case-insensitive keys).

**Deliverables.** `PolicyDocumentTests.cs` with a `Theory` per rule using inline JSON; error-message assertions that reference the field name; the spec updated if a rule turned out to be missing from the code; fix for a known bug from the 2026-09-17 review: `PolicyDocument.Parse` records the validation error for an `actions` entry whose `action` is missing or `null`, then still dereferences it while building the `Policy`, so callers get `InvalidOperationException`/`NullReferenceException` instead of `PolicyValidationException` (skip policy construction for any entry that produced errors).

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** Tests and fixes cover the rules the spec already states. No new policy-format features: composite policies (`all_of` / `any_of`), per-policy model selection, threshold expressions, and YAML stay out.

**Verification.** Every bullet under "Validation" in `Docs/specs/policy-file-format.md` maps to a named test; `"actions": { "x": {} }` and `"actions": { "x": null }` both throw `PolicyValidationException` naming the option; coverage report shows `PolicyDocument.cs` and `PolicySet.cs` ≥ 95% line coverage. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.2.

#### Prompt 1.2 — Engine latency budget

*Done 2026-09-17; end state recorded in the roadmap changelog. Do not re-run.*

*Can run in parallel with Prompt 1.1 (both depend only on 0.2).*

**Session setup.** Effort: `high` — new public API and cancellation semantics on the engine (budget overrun versus caller cancellation); `Kassad.Abstractions` gains a field, so this shapes what later stages copy.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 1, Sub-phase 1.2)

**Goal.** Let operators cap how long a stage waits on the model, with the overrun handled by each policy's `on_error`.

**Deliverables.** `EvaluationOptions { TimeSpan? Budget }` on `GuardrailEngine` (constructor or `IOptions`); a linked `CancellationTokenSource`; budget overrun yields error verdicts with reason `"budget exceeded"` while the caller's own cancellation still propagates; `StageResult.BudgetExceeded` flag; tests using a `FakeDecisionModel` that delays.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Test: 50 ms budget, 500 ms fake → result in < 100 ms with `BudgetExceeded == true`, fail_closed policies Block, fail_open Allow. Test: caller cancels → `OperationCanceledException` propagates. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.2.

#### Prompt 1.3 — Sample runs against the live API

**Session setup.** Effort: `medium` — exercises already-built components against the live API; deliverables are small fixes plus a `samples/README.md`, and verification is two `curl` calls with known expected statuses.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 1, Sub-phase 1.3)

**Goal.** The reference sample behaves as documented with real verdicts.

**Deliverables.** Any fixes needed for the sample; README `curl` commands verified; a `samples/README.md` with the expected JSON for a benign and an injection prompt (values redacted to ranges, since they'll drift).

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** `TYPESAFE_API_KEY=... dotnet run --project samples/Kassad.Sample.ChatApi` then the two `curl`s from the README return 200 and 403 respectively; log output shows one `Kassad Inbound outcome` line per request with two policy lines. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 0.3, 1.1.

### Phase 2 — ASP.NET Core integration proven

#### Prompt 2.1 — Middleware integration tests

**Session setup.** Effort: `high` — keystone: creates the `Kassad.AspNetCore.Tests` project and the `WebApplicationFactory` pattern that 2.2 shares, and fixes the problem+json content-type bug.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 2, Sub-phase 2.1)
- `Docs/specs/rejection-response.md`

**Goal.** Exercise `KassadInboundMiddleware` through a real pipeline.

**Deliverables.** `tests/Kassad.AspNetCore.Tests/` (xunit + `Microsoft.AspNetCore.Mvc.Testing`, versions added to CPM) using `FakeDecisionModel`; tests for: block → 403 problem+json without `policies`; block with `IncludePolicyIdsInResponse` → `policies` present; review → 200 with `Kassad-Outcome: review` and `GetKassadInboundResult()` populated; non-text content type → engine not called; oversized fail_closed → 413; oversized fail_open → engine not called, 200; `RejectAt = Review` → review rejected. Fix for a known bug from the 2026-09-17 review: `KassadInboundMiddleware.WriteRejectionAsync` sets `application/problem+json` and then calls the two-argument `WriteAsJsonAsync`, which resets `Content-Type` to `application/json; charset=utf-8`; use the overload that takes a `contentType`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** All listed tests exist and pass; the 403 and 413 tests assert `Content-Type` starts with `application/problem+json`; middleware file coverage ≥ 90%. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 1.1.

#### Prompt 2.2 — Handler integration tests

**Session setup.** Effort: `medium` — fan-out inside the test project 2.1 establishes; the deliverables enumerate the cases and verification includes a byte-identical body check.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 2, Sub-phase 2.2)
- `Docs/specs/rejection-response.md`

**Goal.** Exercise `KassadDelegatingHandler` with a stub inner handler standing in for the provider.

**Deliverables.** Tests for: inbound block → synthesized 403 with `kassad_blocked`, provider never called; outbound block → provider called, response replaced; allow → original body intact and readable, `Kassad-Outcome` header present; `text/event-stream` → passed through, warning logged (assert via `FakeLogger`); non-2xx provider response → not evaluated; oversized response fail_closed → 413.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** All listed tests pass; the "allow" test asserts the body bytes are byte-identical to what the stub returned. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 2.1 (shares the test project).

#### Prompt 2.3 — Structured state extraction

**Session setup.** Effort: `high` — new abstraction (`IStateExtractor`) plus provider-shape sniffing and a new spec; it decides what every inbound policy sees from here on.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 2, Sub-phase 2.3)

**Goal.** Evaluate the user's actual message, not the JSON envelope around it.

**Deliverables.** `IStateExtractor { object? Extract(string body, string? contentType, Stage stage) }` in `Kassad.AspNetCore`; built-ins for OpenAI chat completions (`messages[].content`, last user turn plus system prompt as separate fields), Anthropic messages, and plain text/JSON fallback; selection by content type + shape sniffing; `KassadOptions.StateExtractor`; the `OutboundState` record extended with extracted fields; spec `Docs/specs/state-extraction.md`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Tests with captured OpenAI and Anthropic request bodies assert the extracted `user_message` equals the last user turn; an unknown shape falls back to raw body; the sample's policies see `state.user_message` (assert in a live test). Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 2.2.

#### Prompt 2.4 — Sample wired to a real provider

**Session setup.** Effort: `high` — first real external integration (OpenAI chat completions) through the wrapped client; live behavior has to be discovered, not assumed.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 2, Sub-phase 2.4)

**Goal.** Show the outbound stage doing real work.

**Deliverables.** Sample reads `Llm:Provider` (`echo` | `openai`) and, when `openai` with `OPENAI_API_KEY` set, forwards to chat completions through the Kassad-wrapped client; README section on running both modes.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** OpenAI is the provider being guarded here, not a decision model: no LLM-backed `IDecisionModel` (`Kassad.LlmJudge`) and no standalone reverse proxy (`kassad-proxy`). The sample forwards through its own Kassad-wrapped `HttpClient`.

**Verification.** With both keys set, a benign prompt returns the provider's reply plus `Kassad-Outcome` header; logs show `Kassad Outbound outcome` lines. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 2.3.

### Phase 3 — Remaining stages usable, observability in place

#### Prompt 3.1 — Typed tool-call and grounding helpers

*Can run in parallel with Phase 2; needs only 1.2.*

**Session setup.** Effort: `high` — adds public records and extension methods to `Kassad.Abstractions`; the field names become the contract policies are written against.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 3, Sub-phase 3.1)
- `Docs/research/typesafe-api-notes.md`

**Goal.** Make the two code-only stages ergonomic.

**Deliverables.** `ToolCallState(string UserIntent, string ToolName, string ToolSchemaJson, string ArgumentsJson)` and `GroundingState(string Claim, string SourcePassage, string? SourceId)` records in `Kassad.Abstractions`; `GuardrailEngineExtensions.EvaluateToolCallAsync` / `EvaluateGroundingAsync`; sample policies for both stages added to `kassad.policies.json` (commented as illustrative); README "Stages" table updated.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Unit tests show the records serialize into the `state` object with the documented field names; a live test runs one tool-call and one grounding policy and gets typed answers back. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 1.2.

#### Prompt 3.2 — Oversized-body pass-through in the handler

*Can run in parallel with Prompts 2.3, 2.4, 3.1 and 3.3 once 2.2 has passed.*

**Session setup.** Effort: `high` — bounded reads and re-attaching a partially consumed `HttpContent` is subtle plumbing; verification is byte-identical forwarding of a 2 MiB body.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 3, Sub-phase 3.2)

**Goal.** Make `OversizedBodyBehavior = fail_open` work in the handler instead of throwing.

**Deliverables.** Bounded read that stops at `MaxBodyBytes + 1` without consuming the rest; on overflow with fail_open, the original content is re-attached (buffered prefix + remaining stream) and sent unevaluated with a warning; `PROJECT_CONTEXT.md` caveat removed.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Handler test: 2 MiB request with 1 MiB limit and fail_open → provider receives the full 2 MiB byte-identically; fail_closed → 413 and provider not called. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 2.2.

#### Prompt 3.3 — OpenTelemetry traces and metrics

*Can run in parallel with Prompt 3.1 and with Phase 2; needs only 1.2.*

**Session setup.** Effort: `high` — designs the telemetry surface (activity and meter names, tags) that operators and 4.1 depend on, with a new spec document.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 3, Sub-phase 3.3)

**Goal.** Let operators see verdict rates and latency without parsing logs.

**Deliverables.** `ActivitySource("Kassad")` with one activity per `EvaluateAsync` tagged `kassad.stage`, `kassad.outcome`, `kassad.model`, `kassad.had_error`; `Meter("Kassad")` with `kassad.verdicts` counter (tags: stage, policy_id, action, from_error) and `kassad.model.latency` histogram; no OTel package dependency (uses `System.Diagnostics` only); `Docs/specs/telemetry.md`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Test with `ActivityListener` and `MeterListener` asserts one activity and N verdict increments per evaluation; the sample, run with `dotnet-counters`, shows the meter. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 1.2.

#### Prompt 3.4 — Streaming evaluation design note

*Can run in parallel with Prompt 3.2 (same dependency, 2.2).*

**Session setup.** Effort: `high` — a design decision with a stated latency/safety trade-off; no code, but the reasoning is the deliverable.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Phase 3, Sub-phase 3.4)
- `Docs/research/typesafe-api-notes.md`

**Goal.** Decide how streaming responses will be evaluated in a later version, without building it.

**Deliverables.** `Docs/specs/streaming-evaluation.md` comparing: evaluate-on-complete (buffer, deliver late), chunked windows (evaluate every N tokens, ability to cut the stream), and post-hoc (stream through, evaluate after, flag retroactively); a recommendation with the latency/safety trade-off stated; open questions listed. `PROJECT_CONTEXT.md` open question updated to point at the note.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** Design note only. No streaming-evaluation code, buffering, or SSE parsing lands in this sub-phase.

**Verification.** The document exists, has a "Recommendation" section, and is linked from the README "Design notes". Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 2.2.

### Phase 4 — Numbers published

#### Prompt 4.1 — Harness CLI and first dataset adapter

*Can run in parallel with Phase 2 and with Prompts 3.1, 3.2 and 3.4 once 3.3 has passed.*

**Session setup.** Effort: `high` — keystone: establishes the harness CLI, adapter shape, runner, and output schema that 4.2 and 4.3 build on; adds a new dependency (`System.CommandLine`).

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 4, Sub-phase 4.1)
- `eval/README.md`
- `Docs/research/typesafe-api-notes.md`

**Goal.** Run one policy over one labeled dataset and emit per-row results.

**Deliverables.** `Kassad.Eval` CLI (`System.CommandLine`, added to CPM) with `run --dataset <name> --policies <file> --out <path>`; `eval/datasets/deepset-prompt-injections.sh` download script + a C# adapter mapping rows to `(text, label)`; concurrency-limited runner with retry via `TypeSafeClient`; JSON output schema documented in `eval/README.md`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** `kassad-eval run --dataset deepset --policies samples/.../kassad.policies.json --out eval/results/<date>-deepset.json` completes on the full set (or a stated sample) and the file contains one row per input with `probability`, `label`, `latency_ms`, `input_tokens`. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 1.2, 3.3 (latency measured consistently).

#### Prompt 4.2 — Metrics, calibration, and report

*Can run in parallel with Phase 2 and the rest of Phase 3.*

**Session setup.** Effort: `high` — statistical correctness (precision/recall sweep, ROC AUC, calibration bins, percentiles, cost) verified against a hand-computed golden fixture to 3 decimals; errors here become published numbers.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 4, Sub-phase 4.2)
- `eval/README.md`
- `Docs/research/typesafe-api-notes.md`

**Goal.** Turn per-row results into the numbers the README promises.

**Deliverables.** `report --in eval/results/ --format markdown`: per policy, precision/recall/F1 at each configured threshold plus a sweep; ROC AUC; 10-bin calibration table (predicted vs. observed); p50/p95 latency; cost per 1k checks from `input_tokens` × quoted rate (rate and its source printed in the table footer).

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** Golden test: a hand-computed 20-row fixture produces the expected metrics to 3 decimals; the markdown renders on GitHub without layout breakage. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 4.1.

#### Prompt 4.3 — Second inbound dataset and a grounding set

**Session setup.** Effort: `medium` — fan-out of the 4.1 adapter pattern to two more datasets with results committed; the one judgment call, choosing a grounding set from the candidates named, is recorded as a research note.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Prerequisites, Phase 4, Sub-phase 4.3)
- `eval/README.md`

**Goal.** Meet the "≥ 2 inbound datasets" bar and seed the grounding stage.

**Deliverables.** JailbreakBench adapter (behaviors + benign set); ToxicChat adapter if license allows, else documented reason; selection of a grounding dataset (candidates: a subset of a claim-verification set with source passages) recorded in `Docs/research/eval-datasets.md`; results JSON committed.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** Inbound datasets plus one grounding set only. Outbound and tool-call datasets are a 0.2 concern, and raw dataset files are never committed.

**Verification.** `eval/results/` holds ≥ 3 dated files; `report` handles multiple datasets in one table. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 4.2, 3.1 (grounding helper).

#### Prompt 4.4 — README numbers table and threshold guidance

**Session setup.** Effort: `high` — threshold guidance and a drift-tolerance policy for `eval.yml` are judgment calls that end up in the README, which must also regenerate byte-identically.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Definition of Done, Phase 4, Sub-phase 4.4)
- `eval/README.md`

**Goal.** Replace "Not yet measured" with generated numbers and honest guidance.

**Deliverables.** `report --update-readme` writes between `<!-- numbers:start -->` / `<!-- numbers:end -->` markers; a CI job (`eval.yml`, scheduled weekly + on tag) that runs the harness on a fixed sample and fails if numbers drift beyond a stated tolerance; README paragraph explaining what the numbers do and don't mean; sample policy thresholds adjusted to the measured operating points, with the trade-off stated in comments.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Verification.** README shows the table; `git diff` after a second `report --update-readme` run is empty; `eval.yml` green. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 4.3, and every Phase 1–3 sub-phase (the measured thresholds must reflect the shipped engine).

#### Prompt 4.5 — `0.1.0` released

**Session setup.** Effort: `medium` — the same release ritual 0.5 proved, plus changelog and API-file bookkeeping; verification is the Definition of Done checklist and the nuget.org listing.

**Read first:**
- `PROJECT_CONTEXT.md`
- `Docs/roadmap.md` (sections: Goal, Definition of Done, Phase 4, Sub-phase 4.5)

**Goal.** Ship.

**Deliverables.** `CHANGELOG.md` `0.1.0` section; `PROJECT_CONTEXT.md` status → "Released 0.1.0; maintenance + 0.2 planning", open questions pruned; `PublicAPI.Unshipped.txt` contents moved to `PublicAPI.Shipped.txt`; `git tag v0.1.0`.

**Constraints.**
- Stay within the deliverables. Do not refactor unrelated code.
- Do not introduce dependencies not already in `Directory.Packages.props`, except those this prompt's Deliverables name explicitly. If any other new dependency is required, stop and surface it before installing.
- Respect the out-of-scope list at the top of this file.
- Follow the conventions in `CONTRIBUTING.md` and `.editorconfig`, and the dated decisions in `PROJECT_CONTEXT.md`.

**Out-of-scope reminder.** Do not apply for the `Kassad.*` reserved prefix on nuget.org; that happens after 0.1.0.

**Verification.** Every Definition of Done box is checked; nuget.org shows `0.1.0` for all four packages; the GitHub release notes list the changelog. Run this before declaring the sub-phase complete. If a check fails in a way you cannot attribute, surface it — do not re-run hoping for a different result, and never loosen an acceptance criterion or suppress a diagnostic without explicit sign-off.

**On completion.**
- If the change materially alters anything `PROJECT_CONTEXT.md` documents (dependency versions, architectural decisions, new external integrations), update it in the same session.
- Add a one-line changelog entry to `Docs/roadmap.md` if this sub-phase closes a phase end state.

**Depends on:** 4.4, 0.5.

# PROJECT_CONTEXT.md

## Status

Active development, pre-release. Scaffold build-verified and committed 2026-09-17; roadmap 0.1 (owner placeholder, `Authors`, package bumps) merged the same day via PR #9; roadmap 0.2 (unit tests green in CI) done locally the same day with its CI leg pending the PR; nothing published to nuget.org yet. Target: `0.1.0` with a numbers table in the README.

## Current stack

- **Runtime:** .NET 10 SDK `10.0.100` (pinned in `global.json`, `rollForward: latestFeature`); libraries multi-target `net8.0;net10.0`, sample and eval target `net10.0` only
- **Language:** C# `latest` (14 under SDK 10), nullable enabled, warnings as errors, `EnforceCodeStyleInBuild`
- **Web framework:** ASP.NET Core via `FrameworkReference Microsoft.AspNetCore.App` (no package pin; follows the TFM)
- **Serialization:** `System.Text.Json` (in-box). No Newtonsoft anywhere.
- **Key libraries:** `Microsoft.Extensions.{Logging.Abstractions,DependencyInjection.Abstractions,Options,Http}` pinned per-TFM (latest 8.0.x for net8.0, 10.0.12 otherwise) in `Directory.Packages.props`; `MinVer 8.0.0`; `Microsoft.CodeAnalysis.PublicApiAnalyzers 5.6.0`
- **Tests:** xunit `2.9.3` (v2), `xunit.runner.visualstudio 4.0.0` (runs v2 tests; verified 2026-09-17), `Microsoft.NET.Test.Sdk 18.10.1`, coverlet `10.0.1`
- **External API:** TypeSafe System One, `POST https://api.typesafe.ai/v1/systemone`, model `jev-latest`, bearer auth. Early access as of Sept 2026.
- **Build / package tooling:** dotnet CLI, Central Package Management, MinVer from `v*` tags, `dotnet pack` → nuget.org via GitHub Actions Trusted Publishing (OIDC)
- **Hosting / deploy target:** none (library). CI on `ubuntu-latest` + `windows-latest`.

## Architectural decisions

- **2026-09-17** — .NET-first, four packages: `Kassad.Abstractions` (contracts, zero deps) / `Kassad` (engine) / `Kassad.TypeSafe` (client, depends only on Abstractions) / `Kassad.AspNetCore`. Same shape as `Microsoft.Extensions.*.Abstractions`. Reason: the client must be usable without the engine; no official .NET SDK exists for TypeSafe.
- **2026-09-17** — Abstractions are serialization-free. Wire mapping is hand-written in `Kassad.TypeSafe/SystemOneWire.cs`, not attribute-driven. Reason: out-of-order `type` discriminators break `[JsonPolymorphic]` on net8.0; hand mapping is robust to field order and extras.
- **2026-09-17** — Policies are JSON, not YAML. Loader is STJ with snake_case; `schemas/kassad-policies.schema.json` for editors. Reason: zero extra dependency, `IConfiguration`-friendly. YAML is a possible later add-on package, not core.
- **2026-09-17** — `on_error` (`fail_open` | `fail_closed`) is required per policy with no default. Validation rejects its absence. Do not add a default; the asymmetry between read paths and destructive paths is the point.
- **2026-09-17** — One policy = one question; all policies for a stage batch into one `DecisionRequest`. Thresholds live in policy config, never in `instructions`. Noul thresholds are probabilities; Score thresholds are level-index units; Choice uses per-option `actions`.
- **2026-09-17** — `VerdictAction` is ordered (`Allow < Flag < Review < Block`); a stage outcome is the max. Confidence below a floor (`min_confidence`) resolves to `Review`, never `Allow`.
- **2026-09-17** — Engine never throws for model failures; they become per-policy verdicts with `FromError = true`. Only `OperationCanceledException` from the caller's token propagates.
- **2026-09-17** — `TypeSafeClient` is a singleton that pulls an `HttpClient` from `IHttpClientFactory` per call. Reason: typed-client-as-singleton captures one handler forever and breaks DNS/handler rotation.
- **2026-09-17** — Rejection responses omit policy ids unless `IncludePolicyIdsInResponse = true`. Reason: naming the check that fired is reconnaissance for an attacker. Verdicts are always fully logged.
- **2026-09-17** — Apache-2.0. Reason: patent grant matters to enterprise adopters more than MIT's brevity.
- **2026-09-17** — Versioning by MinVer from `v*` tags; releases via NuGet Trusted Publishing. No version numbers or API keys are ever committed.
- **2026-09-17** — The repo ships its own `nuget.config`: `<clear />` plus nuget.org as the only source. Reason: Central Package Management raises NU1507 whenever a machine has more than one feed configured, and warnings are errors; a public library also should not restore from private feeds. Adding a feed requires `packageSourceMapping`.
- **2026-09-17** — Every project generates an XML documentation file, including tests, samples and eval (with `CS1591` suppressed outside `src/`). Reason: IDE0005 (unnecessary usings) only runs in build when `GenerateDocumentationFile` is true, and we want that check everywhere. `dotnet_style_require_accessibility_modifiers` is `for_non_interface_members`, the dotnet-repo convention.
- **2026-09-17** — Repo/project named after Fedmahn Kassad (Hyperion Cantos). Package prefix `Kassad.*`. Do not use `TypeSafe.*` as a prefix anywhere; it's the vendor's.

## Open questions / known caveats

- **Scaffold build-verified. Resolved 2026-09-17.** Written without a .NET SDK present; the first build surfaced four config blockers (illegal `--` in a `Directory.Packages.props` comment, NU1507 from multiple feeds, IDE0040 on interface members, IDE0005 needing `GenerateDocumentationFile`). All fixed; `dotnet build -c Release` and `dotnet test` are green locally on net8.0 and net10.0 with only RS0016 warnings, which 0.4 removes. The rest of roadmap 0.1 landed 2026-09-17: GitHub owner placeholder replaced with `jacob-berendsohn` (git remote owner), `Authors` set to `Jacob Berendsohn` (git identity; was the bare first name), version bumps below. CI on both OSes still has to be observed: the repo is private and had no Actions run before the first PR.
- **Package versions in `Directory.Packages.props` were behind latest. Resolved 2026-09-17.** Bumped in roadmap 0.1 one group at a time, each followed by a full `dotnet build -c Release`: MinVer 6.0.0→8.0.0 with PublicApiAnalyzers 3.3.4→5.6.0 (RS0016 count rose from 446 to 786 because the newer analyzer also reports accessors; still the only code emitted); Microsoft.Extensions.* 10.0.0→10.0.12 for the non-net8.0 group (the net8.0 group was already at the latest 8.0.x patches); Microsoft.NET.Test.Sdk 17.13.0→18.10.1, xunit.runner.visualstudio 3.1.0→4.0.0, coverlet.collector 6.0.4→10.0.1. The 4.0.0 runner executes xunit v2 tests: both test projects pass on both TFMs with the CI test command, coverage collection included, and `dotnet pack` produces all four packages under MinVer 8. Dependabot keeps these current from here.
- **`dotnet pack` at solution level warns for the sample.** Open. `Kassad.Sample.ChatApi` uses `Microsoft.NET.Sdk.Web`, which turns on `WarnOnPackingNonPackableProject`, so CI's pack step prints a code-less "packaging has been disabled" warning for it. Harmless (no diagnostic code, so `TreatWarningsAsErrors` ignores it) but it is the only non-RS0016 warning CI will show. Default assumption: set `WarnOnPackingNonPackableProject` to `false` in `samples/Directory.Build.props` with a one-line comment when 0.5 touches the release path; not done in 0.1 because it is a suppression and needs sign-off.
- **Known bug: rejection content type.** `KassadInboundMiddleware.WriteRejectionAsync` sets `application/problem+json`, then the two-argument `WriteAsJsonAsync` resets it to `application/json; charset=utf-8`, so rejections do not match `Docs/specs/rejection-response.md`. Open. Fix in roadmap 2.1 alongside the integration test that asserts the header; until then the spec describes intent, not behavior.
- **Known bug: loader crash on a malformed `actions` entry.** `PolicyDocument.Parse` records the validation error for an entry whose `action` is missing or `null`, then dereferences it anyway, so callers see `InvalidOperationException`/`NullReferenceException` instead of `PolicyValidationException`. Open. Fix in roadmap 1.1 with its `Theory` case.
- **Inbound state extraction.** The middleware and handler evaluate the raw body as the state. Open. Default assumption: keep raw-body until roadmap 2.3 introduces `IStateExtractor` (OpenAI `messages[]`, Anthropic `messages[]`, plain text).
- **Streaming responses.** Passed through unevaluated with a warning. Open. Default assumption: no buffering of SSE; token-level evaluation is roadmap 3.4, design TBD.
- **Oversized body with `fail_open` in the DelegatingHandler** throws because content is already consumed. Open. Default assumption: `fail_closed` is the recommended setting; pass-through is roadmap 3.2.
- **Public API baseline.** `RS0016/RS0017/RS0026/RS0027` are warnings, not errors, until `PublicAPI.Unshipped.txt` is populated (roadmap 0.4). After that, remove `WarningsNotAsErrors` in `src/Directory.Build.props`.
- **`AnalysisLevel` is `latest`, not `latest-recommended`.** Raise in roadmap 0.4 alongside the API baseline and fix what fires.
- **TypeSafe wire format is early-access.** Fixtures in `tests/Kassad.TypeSafe.Tests/Fixtures/` were transcribed from the public API reference, not recorded from live traffic. Default assumption: replace with recorded responses in roadmap 0.3; treat any live/fixture mismatch as a bug in the fixture first.
- **Thresholds in the sample policy file are illustrative.** No eval has been run. Do not present them as recommendations anywhere.

## Repository layout

```
/src/Kassad.Abstractions/   contracts only — no dependencies, no serialization attributes
/src/Kassad/                engine; Policies/ (model + JSON loader + validation), Engine/ (resolver, engine)
/src/Kassad.TypeSafe/       HTTP client; SystemOneWire.cs is the only place that knows the JSON shape
/src/Kassad.AspNetCore/     middleware + DelegatingHandler + DI extensions
/tests/                     xunit; Kassad.TypeSafe.Tests/Fixtures/ holds recorded wire JSON
/samples/Kassad.Sample.ChatApi/  runnable minimal API; kassad.policies.json is the reference policy file
/eval/                      harness (stub) + datasets/ scripts + results/ (committed JSON); data/ is git-ignored
/schemas/                   JSON Schema for policy files, referenced by $schema in policy documents
/Docs/                      see Docs/README.md
```

`src/`, `tests/`, `samples/`, `eval/` each have a `Directory.Build.props` that imports the root one. Package metadata and analyzers are configured there, not per-csproj. The root `nuget.config` pins nuget.org as the only package source.

## Docs/ index

- `Docs/roadmap.md` — phased plan to `0.1.0`, one sub-phase per agent session.
- `Docs/project-prompts.md` — one prompt per roadmap sub-phase, generated from the roadmap by `generate-phase-prompts`; regenerate when the roadmap changes, never edit in place.
- `Docs/specs/policy-file-format.md` — the policy JSON format, field by field, with validation rules.
- `Docs/specs/rejection-response.md` — shape of the 403 problem+json and the handler's synthesized error.
- `Docs/research/typesafe-api-notes.md` — what we know about the System One API, with source URLs.

## Environment / secrets

- `TYPESAFE_API_KEY` — bearer token for api.typesafe.ai. Read by `TypeSafeClientOptions` when `ApiKey` is unset. Required by the sample and by live tests; absent in unit tests by design.
- `NUGET_USER` — GitHub Actions secret: nuget.org profile name used by `NuGet/login@v1` for Trusted Publishing. Not an API key.
- `TYPESAFE_API_KEY` (Actions secret) — enables the `live` CI job on pushes to the owner's repo only.

## Changelog

- **2026-09-17** — PROJECT_CONTEXT.md created with the initial scaffold. Docs/ directory scaffolded. Roadmap created for "publish Kassad 0.1.0 with a numbers table".
- **2026-09-17** — Scaffold build-verified: four restore/compile blockers fixed, `nuget.config` added, two decisions recorded, two known bugs logged for roadmap 1.1 and 2.1. Baseline committed.
- **2026-09-17** — Phase prompts generated from roadmap.
- **2026-09-17** — Roadmap 0.1 done locally: GitHub owner placeholder replaced with `jacob-berendsohn` in nine files, `Authors` set to `Jacob Berendsohn`, every pin in `Directory.Packages.props` at the latest stable release with a green build after each group and tests green on both TFMs. Package-version caveat resolved; sample pack-warning caveat added. CI leg awaits the first PR.
- **2026-09-17** — Roadmap 0.2 done locally: both test projects green on net8.0 and net10.0 under the CI command (`--filter "Category!=Live"`, coverage collected) with no network access; `ci.yml` trx logger moved from `LogFileName=results.trx` to `LogFilePrefix=results` (per-TFM runs no longer overwrite each other; files land as `results_<tfm>_<timestamp>.trx` under each project's `TestResults/`, matched by the existing upload glob); `CONTRIBUTING.md` now names the CI filters that enforce the Live trait. Verified that `--filter "Category=Live"` with no Live tests exits 0, so the `live` job on `main` is safe until 0.3. CI leg awaits the PR.

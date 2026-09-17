# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [SemVer](https://semver.org/).
Versions come from git tags via MinVer; nothing here is hand-numbered until a tag exists.

## [Unreleased]

### Added
- Repository scaffold: four packages (`Kassad.Abstractions`, `Kassad`, `Kassad.TypeSafe`, `Kassad.AspNetCore`), tests, sample, eval stub, CI and Trusted Publishing release workflow.
- `Kassad.Abstractions`: `IDecisionModel`, Noul/Choice/Score question and answer records, `Verdict`, `StageResult`, `ErrorPolicy`.
- `Kassad`: JSON policy loader with fail-fast validation, `GuardrailEngine` batching one request per stage, `VerdictResolver`, structured logging.
- `Kassad.TypeSafe`: client for `POST /v1/systemone` with retry/backoff on 429/529, typed exceptions, DI registration via `AddTypeSafe()`.
- `Kassad.AspNetCore`: `UseKassadInbound()` middleware, `AddKassadHandler()` for provider `HttpClient`s, problem+json rejections.
- `schemas/kassad-policies.schema.json` for editor validation of policy files.
- `Kassad`: `EvaluationOptions.Budget` caps how long a stage waits on the decision model. When it runs out the model call is cancelled, every policy for the stage resolves through its `on_error` with a reason starting `budget exceeded`, and `StageResult.BudgetExceeded` is set; the caller's own cancellation still propagates as `OperationCanceledException`. Configure it with `AddKassad(..., o => o.Budget = ...)` or bind `EvaluationOptions` from configuration; `VerdictResolver.FromBudgetExceeded` is public alongside `FromError`.
- `Kassad.Abstractions`: `StageResult.BudgetExceeded`.

### Changed
- `Kassad.TypeSafe`: HTTP 400 responses now raise `TypeSafeRequestException` (previously the base `TypeSafeException`), the same as 422. The API returns 400 for semantic validation failures such as an unknown model or a question with neither instructions nor criteria; neither status is retried.
- `Kassad`: the `GuardrailEngine` constructor takes an `IOptions<EvaluationOptions>?` ahead of the optional logger, so a logger passed positionally must now be named (`logger: ...`). `Kassad` depends on `Microsoft.Extensions.Options`.

### Fixed
- `Kassad`: `PolicySet.FromJson` and `PolicySet.FromFile` now throw `PolicyValidationException` for every malformed document. An `actions` entry without an `action` (or set to `null`), a `null` entry in `policies`, a non-string `true`/`false` in noul `criteria`, or a non-string level in score `criteria` used to escape as `InvalidOperationException` or `NullReferenceException` after the error had already been recorded. A non-string choice description was silently read as `null` and a `null` score level as an empty string; both are now rejected, as the spec and schema already required.

[Unreleased]: https://github.com/jacob-berendsohn/kassad/compare/HEAD...HEAD

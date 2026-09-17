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

[Unreleased]: https://github.com/__GITHUB_OWNER__/kassad/compare/HEAD...HEAD

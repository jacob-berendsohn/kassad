# Project Documentation

This directory holds every project-specific reference document, planning artifact, design note, screenshot, and supporting material that is not source code. The agent reads files here when the user references them by name in a prompt.

## Layout

- `audit/` — Dated snapshots of the codebase produced by the `project-audit` skill. Append-only, never edited after the fact.
- `specs/` — Feature specifications, design documents, migration plans. Living documents; update them in place.
- `research/` — Third-party API documentation, comparison tables, evaluations of alternatives, anything answering "what are our options."
- `screenshots/` — Images referenced from specs or research. Filename should describe the content, not the date taken.
- `meeting-notes/` — Outputs from synchronous discussions worth preserving across sessions. Dated filenames.

## Conventions

- **Filenames are kebab-case.** `policy-file-format.md`, not `PolicyFileFormat.md` or `policy_file_format.md`.
- **Dated files lead with the date.** `2026-09-17-architecture.md`, not `architecture-2026-09-17.md`. This makes chronological sorting work.
- **Markdown is the default format.** Anything that can be Markdown should be. Use other formats only when content demands it (diagrams in `.drawio`, exports in `.csv`).
- **Reference, don't duplicate.** If a doc points at another doc, link to it rather than copying its content.

## What does NOT belong here

- `CLAUDE.md` and `PROJECT_CONTEXT.md` stay at the repository root, not here.
- Source code, tests, configuration files, build outputs. They stay in their normal project locations.
- **Secrets, credentials, connection strings, API keys, customer PII.** This directory is committed to source control. Treat it accordingly.
- Eval datasets and results. Those live under `eval/` because they are inputs to and outputs of the build.

## Index of significant documents

*Updated as significant documents are added. Each entry is one line.*

- `roadmap.md` — phased plan to `0.1.0`; consumed by `generate-phase-prompts`.
- `project-prompts.md` — one prompt per roadmap sub-phase, generated from `roadmap.md`; regenerate rather than edit.
- `specs/policy-file-format.md` — the policy JSON format and its validation rules.
- `specs/rejection-response.md` — HTTP shape of rejections from the middleware and the handler.
- `specs/telemetry.md` — the `Kassad` activity source and meter: activity and instrument names, tags, units, status semantics, what is not emitted (roadmap 3.3).
- `specs/state-extraction.md` — how a request or response body becomes the state the policies judge: the `IStateExtractor` contract, the default selection by content type and shape, the `user_message` / `system_prompt` / `completion` fields, what is not extracted (roadmap 2.3).
- `research/typesafe-api-notes.md` — System One API facts with source URLs, plus what the wire actually returned when the test fixtures were recorded (roadmap 0.3).

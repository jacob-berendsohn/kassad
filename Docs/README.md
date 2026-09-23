# Project Documentation

This directory holds the project's reference documents that are not source code: specifications, research notes and the roadmap.

## Layout

- `specs/` — Feature specifications, design documents, migration plans. Living documents; update them in place.
- `research/` — Third-party API documentation, comparison tables, evaluations of alternatives, anything answering "what are our options."

## Conventions

- **Filenames are kebab-case.** `policy-file-format.md`, not `PolicyFileFormat.md` or `policy_file_format.md`.
- **Dated files lead with the date.** `2026-09-17-architecture.md`, not `architecture-2026-09-17.md`. This makes chronological sorting work.
- **Markdown is the default format.** Anything that can be Markdown should be. Use other formats only when content demands it (diagrams in `.drawio`, exports in `.csv`).
- **Reference, don't duplicate.** If a doc points at another doc, link to it rather than copying its content.

## What does NOT belong here

- `PROJECT_CONTEXT.md` stays at the repository root, not here.
- Source code, tests, configuration files, build outputs. They stay in their normal project locations.
- **Secrets, credentials, connection strings, API keys, customer PII.** This directory is committed to source control. Treat it accordingly.
- Eval datasets and results. Those live under `eval/` because they are inputs to and outputs of the build.

## Index of significant documents

*Updated as significant documents are added. Each entry is one line.*

- `roadmap.md` — the phased plan `0.1.0` was built to, with its Definition of Done and changelog.
- `specs/policy-file-format.md` — the policy JSON format and its validation rules.
- `specs/rejection-response.md` — HTTP shape of rejections from the middleware and the handler.
- `specs/telemetry.md` — the `Kassad` activity source and meter: activity and instrument names, tags, units, status semantics, what is not emitted (roadmap 3.3).
- `specs/state-extraction.md` — how a request or response body becomes the state the policies judge: the `IStateExtractor` contract, the default selection by content type and shape, the `user_message` / `system_prompt` / `completion` fields, what is not extracted (roadmap 2.3).
- `specs/streaming-evaluation.md` — design note: how a later version evaluates `text/event-stream` responses; evaluate-on-complete, chunked windows and post-hoc compared, the recommendation with its latency/safety trade-off, and the open questions (roadmap 3.4).
- `research/typesafe-api-notes.md` — System One API facts with source URLs, plus what the wire actually returned when the test fixtures were recorded (roadmap 0.3).
- `research/eval-datasets.md` — the public datasets the eval harness runs (deepset, JailbreakBench behaviors, ToxicChat, VitaminC): what each label means, licenses, how rows become states, and the grounding-set comparison and choice (roadmap 4.3).

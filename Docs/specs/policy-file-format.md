# Policy file format

Status: implemented in `src/Kassad/Policies/PolicyDocument.cs` and validated in `PolicySet.Validate`. Schema: `schemas/kassad-policies.schema.json`. Every rule under Validation has a named test in `tests/Kassad.Tests/PolicyDocumentTests.cs` (roadmap 1.1, 2026-09-17).

A policy file is a JSON object with one required key, `policies`, an array of policy objects. Comments (`//`, `/* */`) and trailing commas are tolerated. Property names are `snake_case` and case-insensitive on read.

## Policy object

| Field | Required | Type | Notes |
|---|---|---|---|
| `id` | yes | string | Unique within the file, compared case-insensitively. Becomes the question key and appears in every log line. Convention: `snake_case`. |
| `stage` | yes | `inbound` \| `outbound` \| `tool_call` \| `grounding` | Where the engine runs it. |
| `type` | yes | `noul` \| `choice` \| `score` | Which primitive. Decides the shape of `criteria` and which of `thresholds` / `actions` applies. |
| `instructions` | yes | string | The single narrow question. Natural language. The model sees this and `criteria`; it never sees `id` or `description`. |
| `criteria` | depends | see below | Per-type rubric. |
| `thresholds` | noul, score | object | Ascending cut points; see below. Forbidden on `choice`. |
| `actions` | choice | object | Option → `{ action, min_confidence? }`. Forbidden on `noul`/`score`. |
| `min_confidence` | no | number 0–1 | Choice/Score only. Confidence below this → `review`, regardless of answer. Default 0 (off). Ignored for Noul, which has no confidence. |
| `on_error` | yes | `fail_open` \| `fail_closed` | What the verdict is when the model call fails. **No default. Ever.** |
| `description` | no | string | For humans. |

## `criteria` by type

- **noul** — optional object `{ "true": "...", "false": "..." }` describing what a yes and a no mean. Either key may be omitted or `null`; a value that is present must be a string.
- **choice** — object of at least two `option: description` pairs. Each description is a string or `null`.
- **score** — array of at least two strings, ordered lowest to highest. Index 0 is the first level. Every entry must be a string; `null` is not a level.

## `thresholds`

```json
"thresholds": { "flag": 0.40, "review": 0.60, "block": 0.85 }
```

Resolution: value ≥ `block` → **Block**; else ≥ `review` → **Review**; else ≥ `flag` → **Flag**; else **Allow**. Any key may be omitted; at least one must be present; present values must be strictly ascending in the order flag < review < block.

Units: for `noul`, the yes-probability (0–1). For `score`, the score value in level-index units (0 to `criteria.length - 1`); a score of 1.6 on a four-level rubric means "between level 1 and level 2, closer to 2".

## `actions` (choice only)

```json
"actions": { "prohibited": { "action": "block", "min_confidence": 0.70 } }
```

If the chosen option has an entry and the answer's confidence ≥ `min_confidence`, the verdict is `action`. If confidence is below the rule's floor, the verdict is **Review** (not the rule's action, not Allow). Options without an entry resolve to **Allow**. Every option named in `actions` must exist in `criteria`.

## Validation

- file is valid JSON with a top-level `policies` array of at least one object
- `id`, `stage`, `type`, `instructions`, `on_error` present; `id` unique
- `criteria` matches the type's shape (see above), including that its values are strings
- `thresholds` present with ≥ 1 key, in range, ascending — for noul/score; absent for choice
- `actions` present with ≥ 1 entry, each naming a real option and specifying an `action` — for choice; absent for noul/score
- `min_confidence` and each `actions[*].min_confidence` in [0, 1]

Loading a file that fails any rule throws `PolicyValidationException` listing every problem. `AddKassad("path")` therefore fails the host at startup, which is the intended behavior.

Errors are collected in two passes, and each pass reports everything it found before the next one runs. Reading the document collects every structural problem at once (invalid JSON, an entry that is not an object, a missing `stage`, `type`, `instructions` or `on_error`, a `criteria` shape mismatch, an `actions` entry without an `action`) and throws if there were any. Once every entry reads cleanly, the remaining rules (`id` present and unique, at least one policy, ranges, ordering, and the type-specific presence or absence of `thresholds` and `actions`) are checked together on the whole set. A file therefore needs at most two rounds of fixes, and a malformed document never surfaces as anything other than `PolicyValidationException`.

## Non-goals

- Composite policies (`all_of`, `any_of`). Compose in code from separate policies.
- Per-policy model selection. One `IDecisionModel` per engine.
- Threshold expressions or references. Numbers only.

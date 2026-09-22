# TypeSafe System One API — working notes

Collected 2026-09-17 from the public docs and, the same day, checked against the wire (roadmap 0.3; see
"Verified against the wire" below). Re-verify anything here before relying on it; the product is early-access
and moving.

## Sources

- Docs root: https://docs.typesafe.ai/introduction — machine-readable index at https://docs.typesafe.ai/llms.txt
- API reference: https://docs.typesafe.ai/api (request/response shapes, error codes); markdown at https://docs.typesafe.ai/api.md
- Models: https://docs.typesafe.ai/models (aliases, `GET /v1/models`)
- Confidence: https://docs.typesafe.ai/confidence
- Patterns: https://docs.typesafe.ai/patterns (fan-out, confidence-gated routing, composite scoring, intent routing)
- Cookbooks most relevant to Kassad: `cookbooks/llm_guardrails`, `cookbooks/citation_check`, `cookbooks/classifying_rag_passages`, `cookbooks/function_calling`, `cookbooks/consistency_noul_cookbook`
- Official SDKs: Python and JavaScript only. JS SDK is MIT on GitHub (`typesafe-ai/typesafe-sdk-js`); its `index.d.mts` is the type-level contract worth diffing our wire code against. The Python SDK's response types are at https://docs.typesafe.ai/sdk/python/api/types/responses
- `typesafe-ai/system-one-adapter-python` — vendor-published adapter that answers System One questions using OpenAI/Anthropic under the hood. Design reference for a future `Kassad.LlmJudge` fallback `IDecisionModel`.

## Endpoint

`POST https://api.typesafe.ai/v1/systemone` — `Authorization: Bearer <key>`, `Content-Type: application/json`. Model route `jev-latest`. Both official SDKs read `TYPESAFE_API_KEY` from the environment; we do the same.

Request: `{ "state": string | object | array, "model": string, "questions": { "<key>": Question } }`. Keys are caller-chosen, not sent to the model, echoed in `answers`.

Question types (`type` field): `noul` (optional `criteria: { true, false }`), `choice` (`criteria: { option: description | null }`), `score` (`criteria: [level, ...]`, documented minimum 2). All share `instructions`, which the reference types as `string | object | array`; Kassad only sends strings.

Response: `{ "model", "answers": { "<key>": Answer }, "usage": { "input_tokens", "output_tokens" } }`. Every answer echoes its `type`. Noul answer: `{ type, noul }` — **no confidence**. Choice: `{ type, choice, probabilities, confidence }`. Score: `{ type, score, legend: { "0": "...", ... }, probabilities: { "0": p, ... }, confidence }`. Score probabilities/legend are keyed by level index as strings; we reorder by parsed index and never trust property order. The Python SDK types `usage.input_tokens`/`output_tokens` as `int | None`, so treat them as optional; our reader falls back to 0.

Errors, as the API actually behaves (the reference lists only 401/422/429/529):

| Status | When | Body |
|---|---|---|
| 400 | A rule the schema cannot express: unknown `model`, a noul question with neither instructions nor criteria, an empty question key, a body that is not JSON | `{ "detail": "<message>" }` or `{ "detail": { "error_type": "api_usage_error", "message": "<message>" } }` |
| 401 | Missing or invalid key | not recorded |
| 422 | Schema violation: missing `state`, `state` that is not string/object/array, empty `questions`, unknown question `type`, `criteria` of the wrong shape | `{ "detail": [ { "type", "loc": ["body", ...], "msg", "input", "ctx"? }, ... ] }` — pydantic style; `loc` names the field |
| 429 | Rate limit | docs: retry with exponential backoff |
| 529 | Overloaded | docs: retry with exponential backoff |

We also retry 502/503/504 on the assumption they are transient edge failures. 400, 401 and 422 are never retried.

Every response carries an `x-typesafe-request-id: req_...` header (the Python SDK exposes it as `request_id`). Kassad does not surface it yet; quote it to the vendor when reporting a problem. No rate-limit headers were observed on successful responses.

`GET /v1/models` (bearer auth) returns `{ "models": [ { "name", "description", "release_date" } ] }`. On 2026-09-17 it listed `jev-latest` ("the latest iteration of TypeSafe's System One Model: Jev", released 2026-09-10) and `jev-preview` ("should be better in most ways"). Kassad does not call it.

## Verified against the wire (2026-09-17)

Recorded with `FixtureRecorder` in `tests/Kassad.TypeSafe.Tests` into `Fixtures/` (each file carries `recorded_at`, `recorded_status`, `recorded_request_id`, then the untouched wire properties):

- `response-all-types.json` — one noul, one choice, one score against a string state. The response `model` is the **resolved release**, `jev-1.13.0`, not the `jev-latest` alias we sent. Shapes match the reference exactly: `type` echoed on every answer, no `confidence` on noul, score `legend` and `probabilities` keyed `"0"`, `"1"`, `"2"` in index order, `legend` values identical to the `criteria` we sent. Choice `probabilities` came back in a different key order than the options were sent (`technical`, `sales`, `billing`), so nothing may depend on property order. Values are rounded to two decimals; the score equals the probability-weighted level index within rounding. `output_tokens` is non-zero (73 for three questions), so output is counted even if it is not billed.
- `response-422-numeric-state.json` — `"state": 42`. Three `detail` entries, one per union member (`str`, `dict[any,any]`, `list[any]`), each with `loc` starting `["body", "state", ...]`.
- `response-400-empty-instructions.json` — noul with `"instructions": ""` and no criteria. `{ "detail": "Noul question must have criteria or instructions: q" }`.

Also observed, not recorded:

- The API is more lenient than its reference: a choice with **one** option, a score with **one** level, duplicate score levels, an empty option key and an empty-string `state` were all accepted with 200. Kassad's abstractions keep the documented minimums (two options, two levels) deliberately; a one-way choice is a bug in the policy, not a question.
- The 422 for an unknown question `type` lists the accepted tags as `noul`, `choice`, `score` **and `bounding_box`**. The bounding-box type is undocumented as of this date and out of scope for Kassad; it hints that the "text input only" claim below is dated.
- `x-envoy-upstream-service-time` was 58–91 ms for single-question and three-question requests alike, consistent with the vendor's parallel-evaluation claim. A header on a handful of requests, not a benchmark.

## Behavioral claims (vendor's, unverified by us)

- Questions in one request are evaluated independently and in parallel; adding questions "barely changes" latency.
- Real-time latency on the order of 150 ms; third-party write-ups cite 70–500 ms.
- ~100x cheaper than an LLM call; a Sept 2026 write-up cites $0.042 per million input tokens, output free ([MarkTechPost, 2026-09-19](https://www.marktechpost.com/2026/09/19/typesafe-ai-releases-jev/), checked 2026-09-22; the docs index at `llms.txt` lists no pricing page). `kassad-eval report` prices checks at this rate, labeled "quoted, verify" (roadmap 4.2).
- Calibration is measured across groups of predictions; it does not guarantee any single answer.
- Text input only. No images, audio, video. (See the `bounding_box` observation above.)
- Choice accepts up to 255 options (per a third-party guide, not the official reference).

Everything in this section belongs in the eval harness's measurement list, not in marketing copy.

## Implications baked into Kassad

- Noul has no confidence → `min_confidence` is ignored for noul policies; threshold on probability.
- One request per stage is the intended usage pattern (fan-out), so `GuardrailEngine` batches.
- Because calibration is population-level, thresholds must be tuned on the operator's data; the sample policy values are placeholders.
- `TypeSafeClient` maps **both 400 and 422** to `TypeSafeRequestException` and retries neither: both mean the caller's request must change. `IDecisionModel.Name` reports the alias we send (`typesafe:jev-latest`); `DecisionResponse.Model` reports the release that answered (`jev-1.13.0`). Log the latter when comparing eval runs.

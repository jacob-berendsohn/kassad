# TypeSafe System One API — working notes

Collected 2026-09-17 from the public docs. Re-verify anything here before relying on it; the product is early-access and moving.

## Sources

- Docs root: https://docs.typesafe.ai/introduction — machine-readable index at https://docs.typesafe.ai/llms.txt
- API reference: https://docs.typesafe.ai/api (request/response shapes, error codes)
- Confidence: https://docs.typesafe.ai/confidence
- Patterns: https://docs.typesafe.ai/patterns (fan-out, confidence-gated routing, composite scoring, intent routing)
- Cookbooks most relevant to Kassad: `cookbooks/llm_guardrails`, `cookbooks/citation_check`, `cookbooks/classifying_rag_passages`, `cookbooks/function_calling`, `cookbooks/consistency_noul_cookbook`
- Official SDKs: Python and JavaScript only. JS SDK is MIT on GitHub (`typesafe-ai/typesafe-sdk-js`); its `index.d.mts` is the type-level contract worth diffing our wire code against.
- `typesafe-ai/system-one-adapter-python` — vendor-published adapter that answers System One questions using OpenAI/Anthropic under the hood. Design reference for a future `Kassad.LlmJudge` fallback `IDecisionModel`.

## Endpoint

`POST https://api.typesafe.ai/v1/systemone` — `Authorization: Bearer <key>`, `Content-Type: application/json`. Model route `jev-latest`. Both official SDKs read `TYPESAFE_API_KEY` from the environment; we do the same.

Request: `{ "state": string | object | array, "model": string, "questions": { "<key>": Question } }`. Keys are caller-chosen, not sent to the model, echoed in `answers`.

Question types (`type` field): `noul` (optional `criteria: { true, false }`), `choice` (`criteria: { option: description | null }`), `score` (`criteria: [level, ...]`, ≥ 2). All share `instructions`.

Response: `{ "model", "answers": { "<key>": Answer }, "usage": { "input_tokens", "output_tokens" } }`. Noul answer: `{ type, noul }` — **no confidence**. Choice: `{ type, choice, probabilities, confidence }`. Score: `{ type, score, legend: { "0": "...", ... }, probabilities: { "0": p, ... }, confidence }`. Score probabilities/legend are keyed by level index as strings; we reorder by parsed index and never trust property order.

Errors: 401 auth, 422 validation (body names the field), 429 rate limit, 529 overloaded. Docs say retry 429/529 with exponential backoff. We also retry 502/503/504 on the assumption they are transient edge failures.

## Behavioral claims (vendor's, unverified by us)

- Questions in one request are evaluated independently and in parallel; adding questions "barely changes" latency.
- Real-time latency on the order of 150 ms; third-party write-ups cite 70–500 ms.
- ~100x cheaper than an LLM call; a Sept 2026 write-up cites $0.042 per million input tokens, output free.
- Calibration is measured across groups of predictions; it does not guarantee any single answer.
- Text input only. No images, audio, video.
- Choice accepts up to 255 options (per a third-party guide, not the official reference).

Everything in this section belongs in the eval harness's measurement list, not in marketing copy.

## Implications baked into Kassad

- Noul has no confidence → `min_confidence` is ignored for noul policies; threshold on probability.
- One request per stage is the intended usage pattern (fan-out), so `GuardrailEngine` batches.
- Because calibration is population-level, thresholds must be tuned on the operator's data; the sample policy values are placeholders.

# Streaming evaluation

Status: design note (roadmap 3.4). Nothing in it is implemented. Today `KassadDelegatingHandler` passes a
`text/event-stream` response through unevaluated with one warning, which the roadmap 2.2 test
`Event_stream_response_is_passed_through_unread_with_a_warning` pins: the provider's response object goes back to
the caller with zero bytes read from its stream, no `Kassad-Outcome` header, and the inbound stage evaluated as usual.
This note decides how a version after `0.1.0` evaluates such a response: it compares the three strategies the
roadmap names, recommends one, states the latency/safety trade-off, and lists what is still open. Option, field and
type names below are proposals for the implementing sub-phase, not contracts; the public API is baselined, so every
name goes through `PublicAPI.Unshipped.txt`, and a policy-file field goes through `policy-file-format.md` and the
schema.

## Where a stream meets Kassad

The handler is a `DelegatingHandler` inside the `HttpClient` an application hands its provider SDK. A stream
changes nothing on the way in: the request body is complete before it is sent, so the inbound stage runs exactly as
for a non-streaming call, and `user_message` and `system_prompt` are known before the first response byte arrives.
It is the outbound stage that has no state: `completion` exists only when the last frame has arrived, and what the
handler does with the frames in between is the whole design question.

Three facts about the handler's position shape every option:

- **The caller is an SDK, not a person.** The OpenAI and Anthropic .NET clients send with
  `HttpCompletionOption.ResponseHeadersRead` or its equivalent, get the `HttpResponseMessage` back from `SendAsync`
  before the body, and parse server-sent events off the content stream, re-streaming text to the application, which
  re-streams it to its user. Every frame Kassad forwards or synthesizes is parsed by that SDK, so a frame Kassad
  writes must be one the provider could have written. The application never sees the `HttpResponseMessage`, so a
  header, an `HttpRequestMessage.Options` entry or a trailing header is invisible to it; the only signals that reach
  SDK-based code are the frames themselves, the log, telemetry, and whatever callback Kassad offers.
- **Kassad has no channel to the end user.** It sits between the application and the provider. It can stop frames
  and it can tell the application what it decided; retracting text a person has read, or explaining a cut in the
  application's UI, is the application's work.
- **What is a stream, as the handler can tell.** Today: a 2xx response whose media type is `text/event-stream`.
  A newline-delimited JSON stream (`application/x-ndjson`, the Ollama shape) has a media type containing `json`, so
  the handler reads it to its end and evaluates it whole, strategy A below by accident, bounded by `MaxBodyBytes`.
  Bedrock's binary event stream is not text and passes through. This note is about SSE; the others are listed under
  the open questions.

The middleware has no outbound role, so an application's own SSE endpoint is untouched by this note. The same three
strategies would apply to it, one layer up, with the application's frames instead of the provider's.

## What the two providers stream

Both shapes the extractor recognises (`state-extraction.md`) stream as SSE. Checked against the providers'
references on 2026-09-18, not against recorded frames; the implementing sub-phase records fixtures the way roadmap
0.3 did for the decision model, and the OpenAI key roadmap 2.4 introduces is where the first ones can come from.

| | OpenAI chat completions | Anthropic messages |
|---|---|---|
| Framing | `data:` lines only, no `event:` names | `event: <name>` plus a `data:` line whose `type` repeats the name |
| One frame | a `chat.completion.chunk` object; `id`, `created` and `model` are the same on every chunk | `message_start`, `content_block_start`, `content_block_delta`, `content_block_stop`, `message_delta`, `message_stop` |
| Text | `choices[].delta.content`; a `refusal` string when the model refuses | `content_block_delta` with `delta.type` `text_delta`; `thinking_delta` for extended thinking, which the extractor does not read |
| Tool calls | `choices[].delta.tool_calls[].function.arguments`, a JSON string in fragments | a `content_block_start` of type `tool_use`, then `input_json_delta` fragments in `partial_json` |
| End of text | the chunk whose `finish_reason` is not `null`: `stop`, `length`, `tool_calls`, `content_filter` (`function_call` is deprecated); `null` on every chunk before it | a `message_delta` carrying `stop_reason` (`end_turn`, `max_tokens`, `stop_sequence`, `tool_use`, `pause_turn`, `refusal`, `model_context_window_exceeded`) and cumulative `usage` |
| Terminal frame | `data: [DONE]`; with `stream_options.include_usage`, a chunk with `usage` and empty `choices` comes first | `message_stop` |
| The provider's own cut | `finish_reason: "content_filter"`: a content filter omitted content | `stop_reason: "refusal"`: Claude declined to respond, sent as a normal 200; the reference tells clients to read `stop_details` |
| Noise | `obfuscation` padding fields on delta chunks when enabled | `ping` events; `error` events (`overloaded_error`, the streaming form of a 529); new event types may be added and clients are told to handle unknown ones gracefully |

Two consequences. First, both vocabularies already contain "the text was stopped by a classifier", so a stream Kassad
cuts can end with a frame every consumer of that provider already handles. Second, frames are verbose: an OpenAI
chunk is one or a few characters of text inside 150 to 250 bytes of JSON, so a buffer of frames is 20 to 50 times
the size of the text in it.

## What the decision model can do

Everything Kassad knows about System One is in `Docs/research/typesafe-api-notes.md`; the parts that bind here:

- One `POST` evaluates one complete `state` against a batch of questions. There is no incremental or session API, so
  judging a stream means sending snapshots of it, each a full round trip.
- Latency is on the order of 150 ms by the vendor's claim; the sample observed 150–570 ms per call with the first
  call after startup the slowest (`samples/README.md`), and `x-envoy-upstream-service-time` of 58–91 ms. Adding
  questions "barely changes" latency, so every outbound policy still fits in one request per snapshot.
- Input is cheap: a September 2026 write-up quotes $0.042 per million input tokens, output free. Requests, not
  tokens, are the resource to watch; rate limits are unknown (roadmap prerequisites).
- Calibration is population-level and, so far, measured on complete texts. Nothing says a probability on a prefix
  tracks the probability on the finished text; that is a measurement Phase 4's harness can make once an outbound
  dataset exists (`0.2` per the roadmap's out-of-scope list).
- `EvaluationOptions.Budget` bounds each call cooperatively and turns an overrun into `on_error` verdicts, and the
  engine never throws for a model failure. Each snapshot inherits both behaviors unchanged.

## Vocabulary

- **Frame**: one server-sent event, from its first line to the blank line that ends it. Kassad forwards frames whole
  and never splits or re-serializes one the provider sent.
- **Prefix**: the completion text accumulated so far, in the order the text deltas arrived.
- **Checkpoint**: one evaluation of the prefix by the outbound policies, as the same `OutboundState` a non-streaming
  response produces, with `completion` holding the prefix. The **terminal checkpoint** runs when the frame that ends
  the text arrives, before that frame is forwarded.
- **Trailing** delivery forwards each frame as soon as it arrives and evaluates behind it. **Gated** delivery holds
  frames until the checkpoint covering them has returned `Allow`.
- **Cut**: Kassad ends the stream early. The frames not yet forwarded are dropped, a terminal frame in the provider's
  own vocabulary goes out, the stream ends, and the provider's response is disposed so generation stops.
- **Exposure**: the characters the caller has received when a cut takes effect.
- **TTFT** is time to first token; `t` is one round trip to the decision model; `r` is the provider's output rate in
  characters per second.

## Strategy A: evaluate on complete (buffer, deliver late)

**Mechanism.** The handler reads the whole event stream as it reads any other body today, up to `MaxBodyBytes` plus
one byte, reassembles the completion from the text deltas, evaluates it once, and then returns either the rejection
the non-streaming path returns or the buffered frames, byte for byte, under the provider's headers. Roadmap 3.2's
`BufferedContent` and `ResumedContent` already do the re-attachment, and its `OversizedBodyBehavior` rule applies
unchanged: a stream whose frames exceed the limit is rejected with 413 under `fail_closed` or passed through
unevaluated from the bytes read under `fail_open`, with the warning.

**What the policies see.** Exactly what they see for a non-streaming response, once, from the reconstructed
completion. The rejection is the 403 of `rejection-response.md`, the outcome header is set, and nothing about the
outbound contract changes.

**Latency.** The caller gets nothing, not even headers, until the provider has finished and one round trip has
passed: the caller asked for a stream and receives a burst. For a 600-token answer at 75 tokens per second that is
about eight seconds of silence followed by everything at once. Client timeouts start to matter: a provider SDK's
network timeout (100 s is a common default) now has to outlast the whole generation, not the gap between frames.

**Safety.** Complete. Nothing reaches the caller before the verdict, and a blocked stream produces the same 403 as a
blocked non-streaming response. Prefix calibration is not a question because no prefix is judged.

**Cost.** One request per response, the same tokens as today. Memory holds every frame, 20 to 50 times the text, so
`MaxBodyBytes` (1 MiB by default) bounds an OpenAI stream at roughly 5,000 chunks.

**When it is right.** When the operator needs the non-streaming guarantee on a code path that happens to stream and
the application cannot simply send `stream: false`. It is a mode, not a design for streaming: an application that
meant to stream will turn Kassad off for that client before it accepts an eight-second first token.

## Strategy B: chunked windows (evaluate every N tokens, cut the stream)

**Mechanism.** The handler wraps the provider's content stream. The wrapper forwards frames at frame boundaries,
appends every text delta to the prefix, and takes a checkpoint when at least `N` new characters have arrived and no
checkpoint is in flight; the terminal checkpoint always runs, holding the frame that ends the text. A checkpoint
whose outcome reaches `RejectAt` cuts the stream. Under trailing delivery frames leave as they arrive; under gated
delivery the wrapper holds them and releases up to the offset the last `Allow` covered.

The checkpoint judges the prefix, not the window. Policy instructions refer to `completion`, and a window alone loses
the context that makes a span harmful (a system prompt leaks across "my instructions are:" and the sentence after
it). Judging the prefix makes every checkpoint the non-streaming question asked of a shorter text; the price is that
the same text is sent again at every checkpoint, quadratic in the length of the completion and bounded by
`MaxBodyBytes`.

The handler counts characters, not tokens: a delta is text, and tokens are the provider's accounting, which arrives,
if at all, in the last frame. Four characters per token is a workable rule for English. With one checkpoint in
flight at a time the scheduler paces itself: when the model is slow, checkpoints are fewer and each covers more text.

**What the policies see.** `OutboundState` with `completion` holding the prefix at mid-stream checkpoints, and at the
terminal checkpoint the completion the non-streaming extractor produces from the reconstructed response, so the
terminal verdict is the non-streaming verdict. Policies that ask about absence ("does the completion carry the
disclaimer?") are false on every prefix; they must run at the terminal checkpoint only, which is the `on_partial`
proposal below.

**Latency.** Trailing: TTFT and throughput unchanged; the frame that ends the text waits one round trip. Gated: the
first release comes after `N` characters plus one round trip, then bursts of about `N` characters, each lagging
generation by up to `N / r + t`; TTFT grows by that much.

**Safety.** Gated: nothing the caller received was ever part of a prefix the policies rejected, so exposure is zero,
and the harm of a wrong `Allow` on a prefix is the same as on a complete text. Trailing: a bound, not a guarantee.
Harmful text that begins right after a snapshot is first seen at the next checkpoint, `max(N, r·t)` characters
later, and cut one round trip after that, so exposure is about `max(N, r·t) + r·t` characters. Against an adversary
who front-loads the payload (a leaked system prompt is the first thing the model says), trailing delivery leaks about
that much per attempt, and the inbound stage, which blocks the injection before the provider is called, remains the
first defence. Against the model drifting (toxicity, PII, invented advice in a long answer), harm grows with length,
and a bound on length is what matters.

**Cost.** About `4L / max(N, r·t)` requests for a completion of `L` tokens, each carrying the prompt fields and the
prefix so far: for `N` of 200 characters and 600 tokens, about a dozen requests and seven times the input tokens of
one evaluation, a few hundredths of a cent at the quoted rate. A cut stops paying the provider for the rest of the
answer. Memory holds the prefix (trailing) or the prefix plus the held frames (gated), never the whole stream.

**Failure modes.** A checkpoint can fail like any evaluation. Applied per checkpoint, a `fail_closed` policy turns a
per-call error rate `e` over `k` checkpoints into about `k·e` streams cut for reasons unrelated to content; twelve
checkpoints at one percent is one stream in nine. The sketch below treats a checkpoint without a verdict as no news
and resolves `on_error` at the terminal checkpoint.

**Where it changes the specs.** A cut is a new rejection shape in `rejection-response.md` (a terminal frame, not a
status code); `on_partial` is a new policy-file field; the checkpoints are new activities and measurements in
`telemetry.md`; `state-extraction.md` gains the reconstructed response as an extractor input.

## Strategy C: post-hoc (stream through, evaluate after, flag retroactively)

**Mechanism.** The handler forwards every frame untouched, accumulates the text on the side, and when the stream
ends evaluates the completion once. The verdict goes to the log, the telemetry and an application callback; the
caller has already received everything, and the stream ended the way the provider ended it.

**What the policies see.** The complete completion, as in strategy A. Kassad can say "this response was blocked" but
not act on it; the application decides what "retroactively" means: not persisting the message, redacting it in its
store, sending its own UI event, queueing a `Review` for a person.

**Latency.** None. The verdict lands about one round trip after the last frame, out of band.

**Safety.** None as prevention: exposure is the whole response. Complete as detection: every stream gets a verdict,
so block rates, `Flag` and `Review` counts and the Phase 4 numbers cover streamed traffic too. It is the audit mode.

**Cost.** One request per response; memory holds the text.

**Where it changes the specs.** Only the verdict channel: a callback or observer the application registers, since
the SDK consumer sees neither headers nor trailers, and `telemetry.md` for how a post-hoc verdict is tagged.

## Side by side

Illustrative arithmetic, not a measurement: a 600-token completion (2,400 characters) at 75 tokens per second
(`r` = 300 characters/s, eight seconds of generation), one round trip `t` = 200 ms, a checkpoint interval `N` = 200
characters, and prompt fields of 150 tokens. Change the inputs and the formulas above give the new numbers.

| | A: buffer | B: gated | B: trailing | C: post-hoc |
|---|---|---|---|---|
| TTFT added | 8.2 s (the whole answer plus `t`) | about 0.9 s (`N / r + t`) | 0 | 0 |
| Delivery | one burst | bursts of about 50 tokens every 0.67 s, up to 0.9 s behind | as generated; the last frame waits up to `t` | as generated |
| Exposure before a cut | 0 | 0 | up to about 260 characters, 65 tokens, a sentence or two (`max(N, r·t) + r·t`) | 2,400 characters (no cut) |
| Detection | complete | complete | complete | complete |
| Decision-model requests | 1 | about 12 | about 12 | 1 |
| Input tokens | about 750 | about 5,400 | about 5,400 | about 750 |
| Provider tokens after a Block | all generated | generation stops at the cut | generation stops at the cut | all generated |
| Memory | every frame (20–50× the text) | prefix plus held frames | prefix | prefix |
| Rejection the caller sees | 403 as non-streaming | 403 if nothing was released yet, else a cut | a cut | none; the verdict is out of band |
| Prefix calibration matters | no | yes | yes | no |
| Streaming as the caller intended | no | degraded | yes | yes |

## Recommendation

**Chunked windows over the prefix, trailing delivery, the terminal checkpoint gated, a cut in the provider's own stop
vocabulary.** The other two strategies are the two ends of the same mechanism and ship as settings of it: buffer
(strategy A) is gated delivery with one checkpoint at the end, and post-hoc (strategy C) is trailing delivery with no
mid-stream checkpoints, which the terminal checkpoint improves on at no latency cost by cutting the stream before its
terminal frame. One wrapper, one accumulator and one scheduler serve all three, so the choice of default is a
configuration decision the eval numbers can revise, not an architecture.

**The trade-off, stated.** Trailing delivery keeps streaming exactly as the caller intended (TTFT and throughput
unchanged, the last frame delayed by one round trip) and buys a bound on exposure, not a guarantee: about one
checkpoint interval plus one round trip of text, roughly `max(N, r·t) + r·t` characters, a sentence or two at
today's rates, can reach the caller before a cut takes effect. Gated delivery makes the guarantee (zero exposure) and
pays for it with a delayed, bursty stream; buffering makes the same guarantee and gives up streaming entirely.
Against an adversary who front-loads the payload, only the gated and buffered settings hold, and the inbound stage is
the first line either way; against a model drifting off course over a long answer, a bound on length is the
protection that matters, and trailing delivery gives it at no cost to the reader. That asymmetry is why trailing is
the default and gated is one option away, and why the option's documentation has to say "bound", never "guarantee".

**Defaults, when it lands.**

1. `Streaming.Mode` defaults to `Windowed` with trailing delivery and no mid-stream checkpoints as soon as the
   mechanism ships: every stream is evaluated at its terminal frame and cut there when the outcome reaches
   `RejectAt`; nothing is passed through unevaluated any more. A guardrail that skips streams once it can judge them
   is the one default this note rules out. The `CHANGELOG.md` entry says a stream now costs one model call and its
   last frame waits for it.
2. The mid-stream interval stays off by default until the eval harness has measured, on an outbound dataset, whether
   verdicts on prefixes track verdicts on the finished text and what `N` buys in exposure against requests. The
   number goes into the option's documentation with its provenance, as the README numbers table will carry its own;
   this note uses 200 characters as an illustration only.
3. `Buffer` and gated delivery are options from the first release, so an operator who needs zero exposure has it
   without waiting for the numbers.
4. Policies join mid-stream checkpoints explicitly (`on_partial`, below). With an interval configured, an `outbound`
   policy that has not said whether it may judge a prefix fails validation at startup rather than being judged, or
   skipped, silently.

**Milestones,** each one sub-phase after `0.1.0`:

- **Tee and terminal checkpoint.** The wrapper, the two SSE dialects, reconstruction of the non-streaming response so
  the existing `IStateExtractor` produces the terminal state, the cut at the terminal frame, the `Buffer` mode over
  3.2's contents, the size bound, the verdict callback. Strategy C plus a terminal cut, in effect.
- **Mid-stream checkpoints.** The scheduler, the mid-stream cut, gated delivery, `on_partial` with its schema and
  validation, the telemetry additions.
- **Numbers.** Prefix calibration and the exposure/request curve from the harness against the live API; the default
  interval, if any, follows from them.

**Out of scope for the note and the milestones:** the OpenAI Responses API, Gemini and the NDJSON dialects (they join
when `state-extraction.md` recognises them), an outbound middleware for the application's own SSE endpoint, and any
change to the inbound stage.

## Sketch for the implementing sub-phase

Not a spec. What follows is the shape the recommendation implies, written down so the open questions have something
to point at.

### Components

- **Frame splitter.** Reads the provider's stream and yields complete frames as byte slices: lines end in LF, CRLF or
  CR, a blank line ends a frame, a line starting with `:` is a comment. Kassad forwards the original bytes, so it needs
  the boundaries in bytes; a parsed view on top is only for its own reading. Hand-written, in `Kassad.AspNetCore`
  beside the extractors, a few dozen lines.
- **Dialect reader.** Recognises the dialect from the first frame (`event:` names with a `type` field, or a
  `chat.completion.chunk`), extracts text deltas, tracks open content blocks and choice indexes, detects the frame
  that ends the text, and reconstructs the non-streaming response object at the end the way both SDKs' accumulate
  helpers do (text concatenated per block or choice; tool-call argument fragments concatenated, then parsed;
  `finish_reason` / `stop_reason` and `usage` from the last frames). An unrecognised dialect falls back per mode:
  `Buffer` evaluates the joined `data` payloads whole, the raw-body rule; `Windowed` passes the stream through with
  today's warning, because a stream Kassad cannot read it also cannot end in vocabulary.
- **Accumulator.** The prefix, capped at `MaxBodyBytes` of text (frames are never held beyond the current one under
  trailing delivery). At the cap, `OversizedBodyBehavior` applies as in 3.2: `fail_closed` cuts, with the warning that
  precedes a 413 today; `fail_open` stops evaluating and keeps forwarding, with the pass-through warning.
- **Scheduler.** Takes a checkpoint when no evaluation is in flight and at least `N` characters arrived since the last
  one; always takes the terminal checkpoint, holding the frame that ends the text and every frame after it. Cancels
  an in-flight checkpoint when the caller disposes the response.
- **Wrapper stream.** The `HttpContent` the handler returns, in the family of `ResumedContent`: forward-only,
  readable once, disposing the provider's content with itself. Under trailing delivery it hands frames on as they
  complete; under gated delivery it queues them and releases through the last `Allow`. A cut interrupts a pending
  read of the provider's stream so it takes effect at once, not at the next provider frame.

### The cut, frame by frame

A cut is a rejection when frames have already left, and the 403 of `rejection-response.md` when none have: under
`Buffer`, and under gated delivery when the first checkpoint blocks, `SendAsync` has not returned yet, and the
synthesized 403 goes back as for a non-streaming response. Otherwise the wrapper drops what it holds, writes the
terminal frames below, returns end of stream, and disposes the provider's response, which stops the provider's
generation and the bill for it.

OpenAI, one chunk per open choice, copying `id`, `created`, `model` and `system_fingerprint` from the stream's own
chunks, followed by the sentinel:

```
data: {"id":"chatcmpl-...","object":"chat.completion.chunk","created":1789689600,"model":"gpt-4o-2024-08-06","choices":[{"index":0,"delta":{},"finish_reason":"content_filter"}]}

data: [DONE]

```

Anthropic, closing every open content block first, with the last cumulative `output_tokens` the stream reported
(from `message_start` when nothing later did):

```
event: content_block_stop
data: {"type":"content_block_stop","index":0}

event: message_delta
data: {"type":"message_delta","delta":{"stop_reason":"refusal","stop_sequence":null},"usage":{"output_tokens":42}}

event: message_stop
data: {"type":"message_stop"}

```

Both are frames the provider itself sends when its own classifier intervenes, so an SDK and an application that
already handle a provider refusal handle a Kassad cut, including an agent loop that dispatches tool calls only when
the stop reason says so. The flip side is attribution: from the frames alone the application cannot tell Kassad's cut
from the provider's; the callback and the log say which it was. The exact bytes are recorded from a live refusal if
one can be provoked and otherwise modelled on the references, and the two .NET SDKs' tolerance for them is tested,
`stop_details` included.

### Signalling the verdict

`Kassad-Outcome` cannot carry a verdict that does not exist when the headers are returned, so a `Windowed` response
carries no outcome header (or a documented `pending`, if roadmap 2.4's decision on the response-fidelity caveat wants
a marker); `Buffer` sets it as the non-streaming path does. The verdict itself reaches the application through:

- **A callback or observer** registered on `KassadOptions` (or as a service), invoked once per stream with the
  terminal `StageResult`, the per-checkpoint results, whether the stream was cut and after how many characters, the
  request URI and the trace id. This is the only channel an SDK-based application can see, and it would also give
  the non-streaming handler path the `Review` visibility the middleware has through `HttpContext.Items` (fidelity
  caveat item (a)).
- **`HttpContext.Items`**, when the handler runs inside an ASP.NET Core request and `IHttpContextAccessor` is
  registered: the outbound analogue of `GetKassadInboundResult()`.
- **`HttpResponseMessage.TrailingHeaders`** and `HttpRequestMessage.Options`, for callers that hold the messages
  themselves.
- **Log and telemetry**, as today, plus the stream-level records below.

### Errors and the budget mid-stream

A mid-stream checkpoint that returns without a verdict (model failure, budget overrun, missing answer) is no news:
trailing delivery keeps forwarding, gated delivery keeps holding, the accumulator keeps accumulating, and the next
checkpoint proceeds. `on_error` resolves at the terminal checkpoint exactly as for a non-streaming response, so
`fail_closed` still means that no stream completes cleanly without a verdict, and a model outage costs a gated stream
its delivery until the end (then a cut, or a release) rather than a cut on the first hiccup. `Budget` applies per
checkpoint unchanged; there is no per-stream budget. An operator who wants `on_error` applied at every checkpoint can
have it as an option; it is not the default because of the `k·e` arithmetic above.

### Policies on prefixes

An outbound policy declares whether it may judge a partial completion:

```json
{ "id": "system_prompt_leak", "stage": "outbound", "type": "noul", "on_error": "fail_closed", "on_partial": "evaluate", "...": "..." }
```

`on_partial`: `evaluate` (judge every checkpoint; a `Block` cuts mid-stream) or `defer` (terminal checkpoint only).
Leak, PII, prohibited-content and toxicity questions are prefix-safe; questions about completeness, the tone of the
whole, or the presence of something (a disclaimer, a citation) are not, and one of them at `Block` would cut every
stream at the first checkpoint. Following `on_error`'s precedent the field has no default where it matters: when
`Streaming.Mode` is `Windowed` with a checkpoint interval set, validation requires every `outbound` policy to declare
it; otherwise it is optional and ignored, so the policy files of non-streaming deployments are untouched. Stages
other than `outbound` reject it.

### Telemetry and logs

Each checkpoint is an `IGuardrailEngine.EvaluateAsync` call and produces its `Kassad.Evaluate` activity, its
`kassad.verdicts` increments and its `kassad.model.latency` sample as `telemetry.md` specifies; a streamed response
therefore contributes one verdict per policy per checkpoint to `kassad.verdicts`, which the spec has to say. The
stream itself gets a parent `Kassad.Stream` activity (tags: the final `kassad.outcome`, the checkpoint count, whether
it was cut, the characters forwarded before the cut, the delivery mode) so the checkpoints nest under it, a
`kassad.streams` counter tagged by final outcome and cut for per-response rates, and a histogram of characters
forwarded before a cut for the exposure bound as observed. The engine logs each checkpoint as it logs any evaluation
(the outcome line at Information, `Allow` verdicts at Debug); the handler adds one Warning per cut and one
Information summary per stream. Volume per stream grows with the checkpoint count and is an open question below.

### Tests to pin

Frames forwarded byte for byte until the cut (the 3.2 fidelity tests extended to frames); the cut frames against
recorded fixtures for both dialects and both SDKs' parsers; the terminal frame held until the verdict and forwarded
verbatim on `Allow`; exposure under trailing delivery measured with a `FakeDecisionModel` delay against a scripted
stub stream (frames released before the cut equal to the bound, never more); zero exposure under gated delivery; the
provider's stream disposed at the cut; `MaxBodyBytes` on the accumulated text under both `OversizedBodyBehavior`
values; a mid-stream error or budget overrun not cutting, and `on_error` applied at the terminal checkpoint;
`on_partial: defer` policies absent from mid-stream requests; the observer receiving the terminal result and the
checkpoint results; `RejectAt = Review` cutting on `Review`; an unrecognised dialect passed through with the 2.2
warning; `ResponseHeadersRead` and `ResponseContentRead` callers; caller cancellation mid-stream cancelling the
in-flight checkpoint; `Buffer` producing the non-streaming 403 and, on overflow under `fail_open`, the 3.2
pass-through.

## Open questions

Each with the default assumption this note works under; none is decided here.

1. **Trailing or gated by default.** Trailing (above). Revisit if the Phase 4 numbers show prefix verdicts too noisy
   to cut on, in which case the terminal checkpoint alone may be the honest default.
2. **Checkpoint unit and interval.** Characters, because tokens are invisible to the handler; the interval from the
   eval harness, off until then. An interval that grows with the prefix would bound requests per response at the
   cost of exposure proportional to what was already shown; a fixed one bounds exposure. Undecided.
3. **`on_partial` as a required field.** Required only when mid-stream checkpoints are configured, optional
   otherwise (above). The alternative, a default of `defer` with a startup warning, judges nothing mid-stream until
   the operator edits the policy file and risks being read as protection it is not.
4. **`on_error` at mid-stream checkpoints.** Deferred to the terminal checkpoint (above), with per-checkpoint
   application as an option. This modulates when `on_error` applies, not whether; it adds no default.
5. **Verdict channel.** A callback or observer, because SDK consumers see nothing else; `HttpContext.Items` as the
   ASP.NET Core convenience. Whether `Kassad-Outcome` is omitted or reads `pending` on a `Windowed` response waits
   for roadmap 2.4's decision on fidelity caveat items (a) and (b), which the same observer could close.
6. **Tool calls in a stream.** Never judged as fragments; the terminal checkpoint evaluates the reconstructed
   response through the existing extractor, so a tool-call-only stream is evaluated whole as its non-streaming
   counterpart is, and the `tool_call` stage remains the check before execution.
7. **SSE parsing.** Hand-written boundaries in bytes, because the forwarded bytes must be the provider's.
   `System.Net.ServerSentEvents` is in the .NET 10 shared framework and a package for net8.0 (not in
   `Directory.Packages.props`, so a new dependency for one TFM); it yields parsed items, not the byte boundaries the
   tee needs, so it would sit on top of the splitter, not replace it.
8. **Dialect recognition.** From the first frame, self-contained; the request's provider, which the inbound
   extractor already determined, is the alternative when the first frame is ambiguous.
9. **Non-SSE streams.** NDJSON is read whole today (strategy A by accident) and Bedrock's binary framing passes
   through; both stay as they are until `state-extraction.md` recognises the shapes.
10. **Telemetry contract.** The `Kassad.Stream` activity and `kassad.streams` counter as sketched, and the wording
    change for `kassad.verdicts`; `telemetry.md` is a contract from `0.1.0` on, so this is a `CHANGELOG.md` entry.
11. **Log volume.** One outcome line per checkpoint at Information. Accept for the first release; lower the
    per-checkpoint line or summarize per stream if operators object.
12. **Final outcome of a stream.** The maximum over all checkpoints, in keeping with "a stage outcome is the max";
    the observer also gets the terminal result, so an application can prefer it.
13. **Options scope.** Global `KassadOptions`, like every option today; per-named-client streaming options when an
    application wants to buffer one provider and stream another.
14. **Prefix calibration.** Unmeasured. The harness needs an outbound dataset (a `0.2` concern per the roadmap) and a
    prefix-truncation mode to report how p(yes) on a prefix relates to the finished text.
15. **Default flip.** `PassThrough` becomes `Windowed` (terminal checkpoint only) in the release that ships the
    mechanism, with the `CHANGELOG.md` entry; the alternative, keeping `PassThrough` until an operator opts in, keeps
    a guardrail silently skipping streams.
16. **Rate limits.** A dozen requests per streamed response against limits the prerequisites call unknown; a 429 is
    retried with backoff inside the budget and otherwise counts as no news mid-stream. Measured in the numbers
    milestone.
17. **Application-side SSE.** An outbound middleware for the application's own event streams is not planned; this
    note's design would apply to it unchanged.

## Sources

- The pass-through today: `src/Kassad.AspNetCore/KassadDelegatingHandler.cs` (`IsEventStream` and the warning) and
  `tests/Kassad.AspNetCore.Tests/KassadDelegatingHandlerTests.cs`
  (`Event_stream_response_is_passed_through_unread_with_a_warning`, roadmap 2.2).
- Decision-model facts and latency: `Docs/research/typesafe-api-notes.md`; observed per-call latency in
  `samples/README.md` (the roadmap 1.3 and 3.3 runs).
- Anthropic Messages streaming (event types, delta types, `error` and `ping` events, unknown event types):
  https://platform.claude.com/docs/en/build-with-claude/streaming; stop reasons including `refusal`:
  https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons. Both read 2026-09-18.
- OpenAI chat completions streaming (`chat.completion.chunk`, `finish_reason` values including `content_filter`,
  `stream_options.include_usage`, `data: [DONE]`): https://platform.openai.com/docs/api-reference/chat/streaming,
  which refused an automated fetch on 2026-09-18; the same fields as mirrored in the Azure OpenAI v1 reference on
  Microsoft Learn were read instead (https://learn.microsoft.com/azure/ai-foundry/openai/reference-preview-latest).
- Server-sent events framing: https://html.spec.whatwg.org/multipage/server-sent-events.html. The .NET parser:
  https://learn.microsoft.com/dotnet/api/system.net.serversentevents (`System.Net.ServerSentEvents.dll` is in the
  `Microsoft.NETCore.App` 10.0 shared framework on the development machine and absent from 8.0 and 9.0).

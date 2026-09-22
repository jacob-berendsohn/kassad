using System.Text.Json.Serialization;

namespace Kassad.Eval.Datasets;

/// <summary>
/// The state an inbound text row is evaluated as: <c>{ "user_message": ... }</c>, the shape <c>Kassad.AspNetCore</c>'s
/// <c>InboundState</c> takes for a chat request that has no system prompt (Docs/specs/state-extraction.md), so the
/// numbers describe what the shipped middleware sends for a single-turn prompt. The harness does not reference the
/// web package; the field name is the documented contract, written here by hand like every other renderer of it.
/// </summary>
/// <param name="UserMessage">The dataset row's text.</param>
internal sealed record UserMessageState([property: JsonPropertyName("user_message")] string UserMessage);

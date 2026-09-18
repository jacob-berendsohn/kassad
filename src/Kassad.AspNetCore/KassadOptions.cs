namespace Kassad.AspNetCore;

/// <summary>Behavior of the middleware and the delegating handler. Policies decide the verdict; these options decide what the HTTP layer does with it.</summary>
public sealed class KassadOptions
{
    /// <summary>Verdict severity at which a request or response is rejected. Default: <see cref="VerdictAction.Block"/>. Set to <see cref="VerdictAction.Review"/> for a stricter deployment.</summary>
    public VerdictAction RejectAt { get; set; } = VerdictAction.Block;

    /// <summary>HTTP status returned when rejecting. Default 403.</summary>
    public int RejectionStatusCode { get; set; } = 403;

    /// <summary>
    /// Whether the rejection body names the policies that fired. Default <c>false</c>: telling an
    /// attacker which check caught them is free reconnaissance. Verdicts are always logged in full.
    /// </summary>
    public bool IncludePolicyIdsInResponse { get; set; }

    /// <summary>Largest body the middleware/handler will read for evaluation. Default 1 MiB.</summary>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// What to do with a body larger than <see cref="MaxBodyBytes"/>. Default <see cref="ErrorPolicy.FailClosed"/> (reject with 413).
    /// Truncating and evaluating a prefix is not offered: an injection at the end of a long body is exactly the case that would slip through.
    /// </summary>
    public ErrorPolicy OversizedBodyBehavior { get; set; } = ErrorPolicy.FailClosed;

    /// <summary>Response header carrying the stage outcome (e.g. <c>allow</c>, <c>review</c>). Set to <c>null</c> to disable. Default <c>Kassad-Outcome</c>.</summary>
    public string? OutcomeHeaderName { get; set; } = "Kassad-Outcome";

    /// <summary>
    /// How a body becomes the state the policies judge. Default <see cref="DefaultStateExtractor.Instance"/>: an OpenAI
    /// chat-completions or Anthropic messages body is reduced to the user's latest message and the system prompt
    /// (<see cref="InboundState"/>) on the way in and to the assistant's reply on the way out, and any other body is
    /// evaluated whole. Set <see cref="RawBodyExtractor.Instance"/> to evaluate every body whole, or your own
    /// <see cref="IStateExtractor"/> for another shape. The middleware and the handler share this instance, so an
    /// extractor for your own endpoint's shape should hand every other body to <see cref="DefaultStateExtractor.Instance"/>.
    /// <c>Docs/specs/state-extraction.md</c> is the spec. Must not be <c>null</c>.
    /// </summary>
    public IStateExtractor StateExtractor { get; set; } = DefaultStateExtractor.Instance;
}

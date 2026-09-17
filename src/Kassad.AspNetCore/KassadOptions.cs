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
}

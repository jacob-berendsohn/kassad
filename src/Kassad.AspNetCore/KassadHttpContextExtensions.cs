using Microsoft.AspNetCore.Http;

namespace Kassad.AspNetCore;

/// <summary>Access the verdicts Kassad attached to the current request.</summary>
public static class KassadHttpContextExtensions
{
    internal const string InboundResultKey = "Kassad.Inbound";

    /// <summary>The inbound <see cref="StageResult"/> for this request, or <c>null</c> if the middleware did not run.</summary>
    /// <remarks>
    /// Use this in endpoints to act on <see cref="VerdictAction.Review"/> or <see cref="VerdictAction.Flag"/>:
    /// the middleware only short-circuits at <see cref="KassadOptions.RejectAt"/> and passes everything else through with the result attached.
    /// </remarks>
    public static StageResult? GetKassadInboundResult(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(InboundResultKey, out var value) ? value as StageResult : null;
    }
}

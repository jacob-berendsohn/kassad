using System.Text;
using Kassad.Engine;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kassad.AspNetCore;

/// <summary>
/// Runs <see cref="Stage.Inbound"/> policies against the request body of your own LLM-facing endpoints.
/// Rejects at <see cref="KassadOptions.RejectAt"/>; otherwise attaches the <see cref="StageResult"/> to
/// <see cref="HttpContext.Items"/> and continues.
/// </summary>
/// <remarks>
/// v0 evaluates the raw body as the state. Structured extraction (pulling the user turn out of an
/// OpenAI-shaped <c>messages</c> array, for example) is roadmap 2.3 via an <c>IStateExtractor</c>.
/// Bodies without a textual content type are passed through untouched.
/// </remarks>
public sealed class KassadInboundMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IGuardrailEngine _engine;
    private readonly KassadOptions _options;
    private readonly ILogger<KassadInboundMiddleware> _logger;

    /// <summary>Standard middleware constructor.</summary>
    public KassadInboundMiddleware(RequestDelegate next, IGuardrailEngine engine, IOptions<KassadOptions> options, ILogger<KassadInboundMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!HasTextBody(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength is { } length && length > _options.MaxBodyBytes)
        {
            if (_options.OversizedBodyBehavior == ErrorPolicy.FailClosed)
            {
                _logger.LogWarning("Kassad inbound: body of {Length} bytes exceeds MaxBodyBytes {Max}; rejecting (fail_closed)", length, _options.MaxBodyBytes);
                await WriteRejectionAsync(context, StatusCodes.Status413PayloadTooLarge, "Request body too large to evaluate.", null).ConfigureAwait(false);
                return;
            }

            _logger.LogWarning("Kassad inbound: body of {Length} bytes exceeds MaxBodyBytes {Max}; skipping evaluation (fail_open)", length, _options.MaxBodyBytes);
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Request.EnableBuffering(bufferThreshold: (int)Math.Min(_options.MaxBodyBytes, int.MaxValue));
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        context.Request.Body.Position = 0;

        var result = await _engine.EvaluateAsync(Stage.Inbound, body, context.RequestAborted).ConfigureAwait(false);
        context.Items[KassadHttpContextExtensions.InboundResultKey] = result;

        if (_options.OutcomeHeaderName is { } header)
        {
            context.Response.Headers[header] = result.Outcome.ToString().ToLowerInvariant();
        }

        if (result.Outcome >= _options.RejectAt)
        {
            await WriteRejectionAsync(context, _options.RejectionStatusCode, "Request rejected by policy.", result).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool HasTextBody(HttpRequest request)
    {
        if (request.ContentLength is 0 || request.ContentType is null)
        {
            return false;
        }

        var type = request.ContentType;
        return type.Contains("json", StringComparison.OrdinalIgnoreCase)
               || type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || type.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private async Task WriteRejectionAsync(HttpContext context, int status, string detail, StageResult? result)
    {
        var problem = new Dictionary<string, object?>
        {
            ["type"] = "https://github.com/__GITHUB_OWNER__/kassad/blob/main/Docs/specs/rejection-response.md",
            ["title"] = "Rejected by Kassad",
            ["status"] = status,
            ["detail"] = detail,
            ["traceId"] = context.TraceIdentifier,
        };

        if (result is not null && _options.IncludePolicyIdsInResponse)
        {
            problem["policies"] = result.Verdicts.Where(v => v.Action >= _options.RejectAt).Select(v => v.PolicyId).ToArray();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted).ConfigureAwait(false);
    }
}

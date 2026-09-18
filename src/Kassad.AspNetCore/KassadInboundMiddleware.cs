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
/// The body is reduced to the state the policies judge by <see cref="KassadOptions.StateExtractor"/>: by default an
/// OpenAI chat-completions or Anthropic messages body becomes an <see cref="InboundState"/> (the user's latest message
/// and the system prompt) and any other body is evaluated whole. Bodies without a textual content type are passed
/// through untouched.
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

        // Null from the extractor means "no shape I recognise": the whole body is the state, as it was before extraction existed.
        var state = _options.StateExtractor.Extract(body, context.Request.ContentType, Stage.Inbound) ?? body;
        var result = await _engine.EvaluateAsync(Stage.Inbound, state, context.RequestAborted).ConfigureAwait(false);
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
            ["type"] = "https://github.com/jacob-berendsohn/kassad/blob/main/Docs/specs/rejection-response.md",
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

        // The content type travels with the write: the two-argument WriteAsJsonAsync overload resets Content-Type to
        // application/json; charset=utf-8, undoing any value set on the response beforehand (roadmap 2.1).
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }
}

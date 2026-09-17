using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kassad.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kassad.AspNetCore;

/// <summary>
/// Screens traffic on the <see cref="HttpClient"/> you use to call an LLM provider. Runs
/// <see cref="Stage.Inbound"/> policies on the outgoing prompt and <see cref="Stage.Outbound"/> policies
/// on the returned completion. Rejections are surfaced as a synthesized HTTP response with status
/// <see cref="KassadOptions.RejectionStatusCode"/> so provider SDKs treat them as failed calls.
/// </summary>
/// <remarks>
/// Streaming responses (<c>text/event-stream</c>) are passed through unevaluated with a warning; buffering
/// them would defeat streaming. Token-level streaming evaluation is roadmap 3.4.
/// </remarks>
public sealed class KassadDelegatingHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IGuardrailEngine _engine;
    private readonly KassadOptions _options;
    private readonly ILogger<KassadDelegatingHandler> _logger;

    /// <summary>Create the handler. Register with <see cref="KassadHttpClientBuilderExtensions.AddKassadHandler"/>.</summary>
    public KassadDelegatingHandler(IGuardrailEngine engine, IOptions<KassadOptions> options, ILogger<KassadDelegatingHandler> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? requestBody = null;
        if (request.Content is not null && IsText(request.Content.Headers.ContentType))
        {
            requestBody = await ReadBoundedAsync(request.Content, cancellationToken).ConfigureAwait(false);
            if (requestBody is null)
            {
                return Oversized(request, "request");
            }

            var inbound = await _engine.EvaluateAsync(Stage.Inbound, requestBody, cancellationToken).ConfigureAwait(false);
            if (inbound.Outcome >= _options.RejectAt)
            {
                return Rejection(request, Stage.Inbound, inbound);
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode || response.Content is null || !IsText(response.Content.Headers.ContentType))
        {
            return response;
        }

        if (IsEventStream(response.Content.Headers.ContentType))
        {
            _logger.LogWarning("Kassad outbound: streaming response from {Uri} passed through unevaluated", request.RequestUri);
            return response;
        }

        var responseBody = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (responseBody is null)
        {
            response.Dispose();
            return Oversized(request, "response");
        }

        var state = new OutboundState(requestBody, responseBody);
        var outbound = await _engine.EvaluateAsync(Stage.Outbound, state, cancellationToken).ConfigureAwait(false);
        if (outbound.Outcome >= _options.RejectAt)
        {
            response.Dispose();
            return Rejection(request, Stage.Outbound, outbound);
        }

        // Content was buffered by ReadBoundedAsync; re-materialize it so the caller can read it normally.
        var replacement = new StringContent(responseBody, Encoding.UTF8);
        replacement.Headers.ContentType = response.Content.Headers.ContentType;
        response.Content = replacement;

        if (_options.OutcomeHeaderName is { } header)
        {
            response.Headers.TryAddWithoutValidation(header, outbound.Outcome.ToString().ToLowerInvariant());
        }

        return response;
    }

    /// <summary>State handed to outbound policies: the prompt that produced the completion, and the completion.</summary>
    /// <param name="Request">Raw request body sent to the provider, or <c>null</c> if it was not text.</param>
    /// <param name="Response">Raw response body from the provider.</param>
    public sealed record OutboundState(string? Request, string Response);

    private async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } declared && declared > _options.MaxBodyBytes)
        {
            return null;
        }

        var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return bytes.LongLength > _options.MaxBodyBytes ? null : Encoding.UTF8.GetString(bytes);
    }

    private HttpResponseMessage Oversized(HttpRequestMessage request, string which)
    {
        if (_options.OversizedBodyBehavior == ErrorPolicy.FailOpen)
        {
            // Caller asked to let oversized bodies through unevaluated; nothing to reject. Signal with a header instead.
            throw new InvalidOperationException(
                $"Kassad: {which} body exceeded MaxBodyBytes and OversizedBodyBehavior is FailOpen, but the handler has already consumed the content. " +
                "Raise MaxBodyBytes or use FailClosed. (Roadmap 3.2 adds pass-through for oversized bodies.)");
        }

        _logger.LogWarning("Kassad: {Which} body to {Uri} exceeded MaxBodyBytes {Max}; rejecting (fail_closed)", which, request.RequestUri, _options.MaxBodyBytes);
        return Build(request, (int)HttpStatusCode.RequestEntityTooLarge, "kassad_oversized", $"{which} body too large to evaluate", null);
    }

    private HttpResponseMessage Rejection(HttpRequestMessage request, Stage stage, StageResult result)
    {
        _logger.LogWarning("Kassad {Stage}: rejected call to {Uri} with outcome {Outcome}", stage, request.RequestUri, result.Outcome);
        var policies = _options.IncludePolicyIdsInResponse
            ? result.Verdicts.Where(v => v.Action >= _options.RejectAt).Select(v => v.PolicyId).ToArray()
            : null;
        return Build(request, _options.RejectionStatusCode, "kassad_blocked", $"{stage} rejected by policy", policies);
    }

    private static HttpResponseMessage Build(HttpRequestMessage request, int status, string type, string message, string[]? policies)
    {
        var payload = new
        {
            error = new
            {
                type,
                message,
                policies,
            },
        };

        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            RequestMessage = request,
            Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json"),
        };
        return response;
    }

    private static bool IsText(MediaTypeHeaderValue? type)
    {
        var media = type?.MediaType;
        return media is not null
               && (media.Contains("json", StringComparison.OrdinalIgnoreCase)
                   || media.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEventStream(MediaTypeHeaderValue? type) =>
        string.Equals(type?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
}

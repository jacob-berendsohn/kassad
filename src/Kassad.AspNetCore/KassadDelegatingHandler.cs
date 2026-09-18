using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Both bodies go through <see cref="KassadOptions.StateExtractor"/> first: by default an OpenAI chat-completions or
/// Anthropic messages request is reduced to the user's latest message and the system prompt (<see cref="InboundState"/>),
/// the response to the assistant's reply, and the outbound policies see them together as an <see cref="OutboundState"/>;
/// a body of any other shape is evaluated whole. Streaming responses (<c>text/event-stream</c>) are passed through
/// unevaluated with a warning; buffering them would defeat streaming. Token-level streaming evaluation is roadmap 3.4.
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
        object? requestState = null;
        if (request.Content is not null && IsText(request.Content.Headers.ContentType))
        {
            requestBody = await ReadBoundedAsync(request.Content, cancellationToken).ConfigureAwait(false);
            if (requestBody is null)
            {
                return Oversized(request, "request");
            }

            // Null from the extractor means "no shape I recognise": the whole body is the state, as it was before extraction existed.
            requestState = _options.StateExtractor.Extract(requestBody, request.Content.Headers.ContentType?.ToString(), Stage.Inbound) ?? requestBody;
            var inbound = await _engine.EvaluateAsync(Stage.Inbound, requestState, cancellationToken).ConfigureAwait(false);
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

        var responseState = _options.StateExtractor.Extract(responseBody, response.Content.Headers.ContentType?.ToString(), Stage.Outbound);
        var state = OutboundState.From(requestBody, requestState, responseBody, responseState);
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

    /// <summary>
    /// State handed to outbound policies: the prompt that produced the completion, and the completion, each in the
    /// most useful form <see cref="KassadOptions.StateExtractor"/> could give it. Each body appears once: as
    /// <see cref="UserMessage"/> and <see cref="SystemPrompt"/> when the extractor recognised the request, as
    /// <see cref="Completion"/> when it recognised the response, and otherwise whole in <see cref="Request"/> or
    /// <see cref="Response"/>.
    /// </summary>
    /// <remarks>
    /// A decision model that serializes states with <c>System.Text.Json</c>, as <c>Kassad.TypeSafe</c> does, presents
    /// this state as an object with the fields <c>user_message</c>, <c>system_prompt</c>, <c>completion</c>,
    /// <c>request</c> and <c>response</c>, each present only when it has a value, so an outbound policy can refer to
    /// the same names an inbound policy uses. <c>Docs/specs/state-extraction.md</c> is the spec.
    /// </remarks>
    /// <param name="Request">The request as the inbound stage saw it, when that was text and not an <see cref="InboundState"/>: the body sent to the provider, or the text a custom extractor returned for it. <c>null</c> when the request had no text body or when <see cref="UserMessage"/> carries its content.</param>
    /// <param name="Response">The response body from the provider, when the extractor did not recognise it. <c>null</c> when <see cref="Completion"/> carries its content.</param>
    public sealed record OutboundState(string? Request, string? Response)
    {
        /// <summary>The request as the inbound stage saw it, when that was text; see the record's remarks.</summary>
        [JsonPropertyName("request")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Request { get; init; } = Request;

        /// <summary>The response body, when the extractor did not recognise its shape.</summary>
        [JsonPropertyName("response")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Response { get; init; } = Response;

        /// <summary>The user's latest message, when the extractor recognised the request (<see cref="InboundState.UserMessage"/>).</summary>
        [JsonPropertyName("user_message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserMessage { get; init; }

        /// <summary>The system prompt, when the extractor recognised the request and it had one (<see cref="InboundState.SystemPrompt"/>).</summary>
        [JsonPropertyName("system_prompt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SystemPrompt { get; init; }

        /// <summary>The assistant's reply, when the extractor recognised the response.</summary>
        [JsonPropertyName("completion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Completion { get; init; }

        /// <summary>
        /// Folds what the extractor returned for each direction into the state: an <see cref="InboundState"/> becomes
        /// <see cref="UserMessage"/> and <see cref="SystemPrompt"/>, a string from the response side becomes
        /// <see cref="Completion"/>, and a side that was not recognised keeps its whole body.
        /// </summary>
        internal static OutboundState From(string? requestBody, object? requestState, string responseBody, object? responseState)
        {
            var prompt = requestState as InboundState;
            var completion = responseState as string;
            return new OutboundState(
                prompt is null ? requestState as string ?? requestBody : null,
                completion is null ? responseBody : null)
            {
                UserMessage = prompt?.UserMessage,
                SystemPrompt = prompt?.SystemPrompt,
                Completion = completion,
            };
        }
    }

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

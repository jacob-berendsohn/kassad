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
/// <para>
/// Streaming responses (<c>text/event-stream</c>) are passed through unevaluated with a warning; buffering
/// them would defeat streaming. Token-level streaming evaluation is roadmap 3.4.
/// </para>
/// <para>
/// A body is read up to <see cref="KassadOptions.MaxBodyBytes"/> plus one byte, which is enough to know whether it fits.
/// One that does is evaluated and forwarded from the bytes read. One that does not is rejected with 413 under
/// <see cref="ErrorPolicy.FailClosed"/>; under <see cref="ErrorPolicy.FailOpen"/> it is passed through unevaluated
/// with a warning, the bytes already read ahead of whatever remains unread, so nothing is buffered beyond the limit.
/// </para>
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
        if (request.Content is { } requestContent && IsText(requestContent.Headers.ContentType))
        {
            var body = await ReadBoundedAsync(requestContent, cancellationToken).ConfigureAwait(false);
            if (body.Oversized)
            {
                if (_options.OversizedBodyBehavior == ErrorPolicy.FailClosed)
                {
                    return Oversized(request, "request");
                }

                request.Content = PassThrough(request, "request", body);
            }
            else
            {
                requestBody = body.Text;
                request.Content = body.Reattach();

                var inbound = await _engine.EvaluateAsync(Stage.Inbound, requestBody, cancellationToken).ConfigureAwait(false);
                if (inbound.Outcome >= _options.RejectAt)
                {
                    return Rejection(request, Stage.Inbound, inbound);
                }
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

        var responseContent = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (responseContent.Oversized)
        {
            if (_options.OversizedBodyBehavior == ErrorPolicy.FailClosed)
            {
                response.Dispose();
                return Oversized(request, "response");
            }

            response.Content = PassThrough(request, "response", responseContent);
            return response;
        }

        var responseBody = responseContent.Text;
        var state = new OutboundState(requestBody, responseBody);
        var outbound = await _engine.EvaluateAsync(Stage.Outbound, state, cancellationToken).ConfigureAwait(false);
        if (outbound.Outcome >= _options.RejectAt)
        {
            response.Dispose();
            return Rejection(request, Stage.Outbound, outbound);
        }

        // The bounded read consumed the provider's stream; hand the caller the bytes it read under the provider's own content headers.
        response.Content = responseContent.Reattach();

        if (_options.OutcomeHeaderName is { } header)
        {
            response.Headers.TryAddWithoutValidation(header, outbound.Outcome.ToString().ToLowerInvariant());
        }

        return response;
    }

    /// <summary>State handed to outbound policies: the prompt that produced the completion, and the completion.</summary>
    /// <param name="Request">
    /// Raw request body sent to the provider, or <c>null</c> if it was not text or was passed through unevaluated because it
    /// exceeded <see cref="KassadOptions.MaxBodyBytes"/>.
    /// </param>
    /// <param name="Response">Raw response body from the provider.</param>
    public sealed record OutboundState(string? Request, string Response);

    /// <summary>
    /// Read <paramref name="content"/> up to <see cref="KassadOptions.MaxBodyBytes"/> plus one byte. The extra byte tells an
    /// oversized body from one that exactly fits without consuming anything past it; a body whose declared length is already
    /// over the limit is not read at all.
    /// </summary>
    private async Task<BoundedBody> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        long limit = _options.MaxBodyBytes;
        var declared = content.Headers.ContentLength;
        if (declared > limit)
        {
            return BoundedBody.DeclaredOversized(content);
        }

        // A body has to fit in one array to be evaluated, so anything past int.MaxValue - 1 bytes counts as oversized however high the limit is set.
        int cap = (int)Math.Min(limit, int.MaxValue - 1) + 1;
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[(int)Math.Min(cap, (declared ?? 4096) + 1)];
        int length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (length == cap)
                {
                    return BoundedBody.Overflowing(content, buffer, length, stream);
                }

                Array.Resize(ref buffer, (int)Math.Min(2L * buffer.Length, cap));
            }

            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return BoundedBody.Complete(content, buffer, length);
            }

            length += read;
        }
    }

    private HttpResponseMessage Oversized(HttpRequestMessage request, string which)
    {
        _logger.LogWarning("Kassad: {Which} body to {Uri} exceeded MaxBodyBytes {Max}; rejecting (fail_closed)", which, request.RequestUri, _options.MaxBodyBytes);
        return Build(request, (int)HttpStatusCode.RequestEntityTooLarge, "kassad_oversized", $"{which} body too large to evaluate", null);
    }

    private HttpContent PassThrough(HttpRequestMessage request, string which, BoundedBody body)
    {
        _logger.LogWarning("Kassad: {Which} body to {Uri} exceeded MaxBodyBytes {Max}; passing through unevaluated (fail_open)", which, request.RequestUri, _options.MaxBodyBytes);
        return body.Reattach();
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

    /// <summary>Copy every content header as it was sent, <c>Content-Length</c> and <c>Content-Type</c> included, without re-validating it.</summary>
    private static void CopyHeaders(HttpContentHeaders from, HttpContentHeaders to)
    {
        foreach (var header in from.NonValidated)
        {
            to.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>
    /// What <see cref="ReadBoundedAsync"/> took from a content, and the content that replaces it. Reading through the content's
    /// stream moves the same stream the content would send from, so once a body has been read it is sent from the
    /// bytes read (<see cref="BufferedContent"/>), or from those bytes followed by the rest of the stream when the read stopped
    /// at the limit (<see cref="ResumedContent"/>). A body oversized by its declared length was never read and stays as it is.
    /// </summary>
    private sealed class BoundedBody
    {
        private readonly HttpContent _content;
        private readonly byte[]? _bytes;
        private readonly int _length;
        private readonly Stream? _remainder;

        private BoundedBody(HttpContent content, byte[]? bytes, int length, Stream? remainder, bool oversized)
        {
            _content = content;
            _bytes = bytes;
            _length = length;
            _remainder = remainder;
            Oversized = oversized;
        }

        /// <summary>True when the body is larger than <see cref="KassadOptions.MaxBodyBytes"/>, by declaration or by measure.</summary>
        public bool Oversized { get; }

        /// <summary>The whole body decoded as UTF-8. Only meaningful when the body was read to its end.</summary>
        public string Text => Encoding.UTF8.GetString(_bytes!, 0, _length);

        public static BoundedBody DeclaredOversized(HttpContent content) => new(content, null, 0, null, oversized: true);

        public static BoundedBody Complete(HttpContent content, byte[] bytes, int length) => new(content, bytes, length, null, oversized: false);

        public static BoundedBody Overflowing(HttpContent content, byte[] bytes, int length, Stream remainder) => new(content, bytes, length, remainder, oversized: true);

        /// <summary>The content to send or return in place of the one read: the original itself when nothing was read from it.</summary>
        public HttpContent Reattach()
        {
            if (_bytes is null)
            {
                return _content;
            }

            return _remainder is null
                ? new BufferedContent(_content, _bytes, _length)
                : new ResumedContent(_content, _bytes, _length, _remainder);
        }
    }

    /// <summary>
    /// A body the bounded read consumed to its end, re-materialized over the bytes it read under the original content's headers.
    /// Readable and sendable any number of times, like any <see cref="ByteArrayContent"/>. Disposing it disposes the content it
    /// stands in for.
    /// </summary>
    private sealed class BufferedContent : ByteArrayContent
    {
        private readonly HttpContent _original;

        public BufferedContent(HttpContent original, byte[] bytes, int length)
            : base(bytes, 0, length)
        {
            _original = original;
            CopyHeaders(original.Headers, Headers);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _original.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// An oversized body under <see cref="ErrorPolicy.FailOpen"/>: the bytes the bounded read had taken, followed by whatever it
    /// left unread in the original content, under the original content's headers. Like a response body straight off the wire it
    /// can be sent or read once; a second attempt throws rather than sending a truncated body. Disposing it disposes the
    /// original content.
    /// </summary>
    private sealed class ResumedContent : HttpContent
    {
        private readonly HttpContent _original;
        private readonly PrefixedStream _body;
        private bool _consumed;

        public ResumedContent(HttpContent original, byte[] prefix, int prefixLength, Stream remainder)
        {
            _original = original;
            _body = new PrefixedStream(prefix, prefixLength, remainder);
            CopyHeaders(original.Headers, Headers);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Consume().CopyToAsync(stream, cancellationToken);

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            Consume().CopyTo(stream);

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(Consume());

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) => Consume();

        protected override bool TryComputeLength(out long length)
        {
            // Only the original content knew its length; the Content-Length header it declared, if any, was copied with the rest.
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _body.Dispose();
                _original.Dispose();
            }

            base.Dispose(disposing);
        }

        private PrefixedStream Consume()
        {
            if (_consumed)
            {
                throw new InvalidOperationException("Kassad: this body was passed through unevaluated and has already been read; it cannot be read again.");
            }

            _consumed = true;
            return _body;
        }
    }

    /// <summary>
    /// Read-only, forward-only stream over a buffered prefix followed by the rest of another stream. Disposing it disposes
    /// that stream.
    /// </summary>
    private sealed class PrefixedStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly int _prefixLength;
        private readonly Stream _remainder;
        private int _position;

        public PrefixedStream(byte[] prefix, int prefixLength, Stream remainder)
        {
            _prefix = prefix;
            _prefixLength = prefixLength;
            _remainder = remainder;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position < _prefixLength)
            {
                int count = Math.Min(buffer.Length, _prefixLength - _position);
                _prefix.AsSpan(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            return _remainder.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _position < _prefixLength
                ? new ValueTask<int>(Read(buffer.Span))
                : _remainder.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _remainder.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

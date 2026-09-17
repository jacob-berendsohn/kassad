using System.Net;
using System.Net.Http.Headers;
using Kassad.Policies;
using Kassad.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Kassad.AspNetCore.Tests;

/// <summary>
/// Wires <see cref="KassadDelegatingHandler"/> the way an application does: <c>AddKassad</c>, <c>AddKassadAspNetCore</c> and
/// <c>AddHttpClient(...).AddKassadHandler()</c> on a service collection, with a <see cref="StubProvider"/> as the primary
/// handler in place of the LLM provider and a <see cref="FakeLogger{T}"/> receiving the handler's log lines. Every test
/// builds its own harness with the options it needs and disposes it afterwards.
/// </summary>
/// <remarks>
/// Clients come from <see cref="IHttpClientFactory"/>, so each test also goes through the transient handler registration
/// and hits <see cref="KassadOptions"/> validation where a real application does: on the first client it creates.
/// </remarks>
internal sealed class KassadHandlerHarness : IDisposable
{
    /// <summary>Name of the registered client.</summary>
    public const string ClientName = "provider";

    /// <summary>Base address of the stub provider; the handler's log lines quote the full request URI.</summary>
    public static readonly Uri ProviderBaseAddress = new("https://provider.test/");

    private readonly ServiceProvider _services;

    /// <summary>Create a harness for one handler configuration.</summary>
    /// <param name="model">The decision model the engine calls. Its <see cref="FakeDecisionModel.Requests"/> show which stages ran.</param>
    /// <param name="configure">Applied through <c>AddKassadAspNetCore</c>; <c>null</c> keeps the <see cref="KassadOptions"/> defaults.</param>
    /// <param name="policies">
    /// Defaults to <see cref="TestPolicies.Injection"/> (inbound: fail_closed, Flag 0.4 / Review 0.6 / Block 0.85) and
    /// <see cref="TestPolicies.HarmSeverity"/> (outbound: fail_closed, Review 2 / Block 3 over four levels).
    /// </param>
    public KassadHandlerHarness(FakeDecisionModel model, Action<KassadOptions>? configure = null, PolicySet? policies = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));

        var services = new ServiceCollection();
        services.AddSingleton<IDecisionModel>(Model);
        services.AddSingleton<ILogger<KassadDelegatingHandler>>(Logger);
        services.AddKassad(policies ?? PolicySet.FromPolicies([TestPolicies.Injection(), TestPolicies.HarmSeverity()]));
        services.AddKassadAspNetCore(configure);
        services.AddHttpClient(ClientName, client => client.BaseAddress = ProviderBaseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => Provider)
                .AddKassadHandler();

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>The decision model behind the engine.</summary>
    public FakeDecisionModel Model { get; }

    /// <summary>The stub at the end of the chain, standing in for the LLM provider.</summary>
    public StubProvider Provider { get; } = new();

    /// <summary>
    /// Receives what the handler logs and nothing else: the engine resolves its own <c>ILogger&lt;GuardrailEngine&gt;</c>,
    /// which has no provider here.
    /// </summary>
    public FakeLogger<KassadDelegatingHandler> Logger { get; } = new();

    /// <summary>A client from the factory whose chain is <see cref="KassadDelegatingHandler"/> and then <see cref="Provider"/>.</summary>
    public HttpClient CreateClient() => _services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

    public void Dispose() => _services.Dispose();
}

/// <summary>
/// Stands in for the LLM provider at the end of the handler chain. Records every request that reached it, reading the
/// body inside the provider the way a real one would, and answers with the scripted response.
/// </summary>
internal sealed class StubProvider : HttpMessageHandler
{
    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = Bodies.Of("{}"u8.ToArray(), "application/json") };

    /// <summary>Every request that reached the provider, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The body bytes of every request that reached the provider, in order; empty for a request without content.</summary>
    public List<byte[]> RequestBodies { get; } = [];

    /// <summary>The most recent response the provider handed back, to compare against what the caller received.</summary>
    public HttpResponseMessage? LastResponse { get; private set; }

    /// <summary>Answer every request with a fresh response of this status and body.</summary>
    /// <param name="status">The status code.</param>
    /// <param name="body">The exact body bytes.</param>
    /// <param name="contentType">The <c>Content-Type</c> header, or <c>null</c> for none.</param>
    /// <param name="declareLength">False sends the body without a <c>Content-Length</c>, the way a chunked response arrives.</param>
    public StubProvider Respond(HttpStatusCode status, byte[] body, string? contentType, bool declareLength = true) =>
        Respond(() => new HttpResponseMessage(status) { Content = Bodies.Of(body, contentType, declareLength) });

    /// <summary>Answer every request with the response this factory builds.</summary>
    public StubProvider Respond(Func<HttpResponseMessage> response)
    {
        _respond = response;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));

        var response = _respond();
        response.RequestMessage ??= request;
        LastResponse = response;
        return response;
    }
}

/// <summary>Request and response bodies over exact bytes.</summary>
internal static class Bodies
{
    /// <summary>Content over <paramref name="bytes"/>, with or without a declared <c>Content-Length</c>.</summary>
    /// <param name="bytes">The exact body bytes.</param>
    /// <param name="contentType">The <c>Content-Type</c> header, or <c>null</c> for none.</param>
    /// <param name="declareLength">
    /// True buffers the bytes so the content declares its length; false streams them from an <see cref="UnseekableStream"/>,
    /// which leaves <c>Content-Length</c> unset.
    /// </param>
    public static HttpContent Of(byte[] bytes, string? contentType, bool declareLength = true)
    {
        HttpContent content = declareLength ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes));
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return content;
    }
}

/// <summary>
/// A read-only, forward-only view over a byte array. <see cref="CanSeek"/> is false, so a <see cref="StreamContent"/>
/// over it declares no <c>Content-Length</c>, the way a chunked or streamed provider response arrives; <see cref="BytesRead"/>
/// tells whether anyone consumed it.
/// </summary>
internal sealed class UnseekableStream : Stream
{
    private readonly MemoryStream _inner;

    public UnseekableStream(byte[] bytes) => _inner = new MemoryStream(bytes, writable: false);

    /// <summary>How many bytes have been read from this stream so far.</summary>
    public long BytesRead { get; private set; }

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
        int read = _inner.Read(buffer);
        BytesRead += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await _inner.ReadAsync(buffer, cancellationToken);
        BytesRead += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

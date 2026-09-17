using System.Net;
using System.Text;

namespace Kassad.TypeSafe.Tests;

/// <summary>Scripted HTTP responses in order; records every request body.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<string> RequestBodies { get; } = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHandler Enqueue(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("StubHandler: no response scripted for this request.");
        }

        return _responses.Dequeue()();
    }

    public static TypeSafeClient Client(StubHandler handler, int maxRetries = 3) =>
        new(new HttpClient(handler), new TypeSafeClientOptions { ApiKey = "test-key", MaxRetries = maxRetries, MaxRetryDelay = TimeSpan.FromMilliseconds(1) });
}

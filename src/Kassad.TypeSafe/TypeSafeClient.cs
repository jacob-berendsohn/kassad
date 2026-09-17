using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Kassad.TypeSafe;

/// <summary>
/// Client for <c>POST /v1/systemone</c>. One call evaluates every question in a
/// <see cref="DecisionRequest"/> against its state and returns typed answers.
/// </summary>
/// <remarks>
/// Thread-safe. Register through <see cref="TypeSafeServiceCollectionExtensions.AddTypeSafe"/> so the
/// <see cref="HttpClient"/> is pooled, or construct directly with your own <see cref="HttpClient"/>.
/// Retries 429/529 and transient transport errors with exponential backoff and jitter, honoring
/// <c>Retry-After</c>. 400, 401 and 422 are never retried.
/// </remarks>
public sealed class TypeSafeClient : IDecisionModel
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used when registered through DI.</summary>
    public const string HttpClientName = "Kassad.TypeSafe";

    private const string EvaluatePath = "v1/systemone";

    private readonly Func<HttpClient> _clientFactory;
    private readonly TypeSafeClientOptions _options;
    private readonly AuthenticationHeaderValue _auth;

    /// <summary>
    /// DI constructor. Resolves a fresh <see cref="HttpClient"/> from the factory per call so handler
    /// rotation keeps working even though this client is registered as a singleton.
    /// </summary>
    public TypeSafeClient(IHttpClientFactory httpClientFactory, IOptions<TypeSafeClientOptions> options)
        : this(
            () => (httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory))).CreateClient(HttpClientName),
            (options ?? throw new ArgumentNullException(nameof(options))).Value)
    {
    }

    /// <summary>Manual constructor. You own the <see cref="HttpClient"/>; its <c>BaseAddress</c> is set from options if unset.</summary>
    public TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions options)
        : this(() => httpClient, options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        httpClient.BaseAddress ??= _options.BaseAddress;
    }

    private TypeSafeClient(Func<HttpClient> clientFactory, TypeSafeClientOptions options)
    {
        _clientFactory = clientFactory;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _auth = new AuthenticationHeaderValue("Bearer", _options.ResolveApiKey());
    }

    /// <inheritdoc />
    public string Name => $"typesafe:{_options.Model}";

    /// <inheritdoc />
    public async Task<DecisionResponse> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = SystemOneWire.WriteRequest(request, _options.Model);
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var http = _clientFactory();

            using var message = new HttpRequestMessage(HttpMethod.Post, EvaluatePath)
            {
                Content = new ByteArrayContent(payload),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            message.Headers.Authorization = _auth;

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                await Task.Delay(Backoff(attempt, retryAfter: null), cancellationToken).ConfigureAwait(false);
                attempt++;
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new TypeSafeException($"TypeSafe request failed after {attempt + 1} attempt(s): {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient timeout surfaces as TaskCanceledException without our token being signaled.
                throw new TypeSafeException($"TypeSafe request timed out after {http.Timeout.TotalSeconds:0.#}s.", ex);
            }

            using (response)
            {
                var status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return await SystemOneWire.ReadResponseAsync(stream, request.Questions, cancellationToken).ConfigureAwait(false);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var retryAfter = response.Headers.RetryAfter?.Delta
                                 ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

                if (IsRetryable(status) && attempt < _options.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt, retryAfter), cancellationToken).ConfigureAwait(false);
                    attempt++;
                    continue;
                }

                throw status switch
                {
                    401 => new TypeSafeAuthenticationException("TypeSafe rejected the API key (401).", status, body),
                    400 or 422 => new TypeSafeRequestException($"TypeSafe rejected the request ({status}): {Truncate(body)}", status, body),
                    429 or 529 => new TypeSafeRateLimitException($"TypeSafe returned {status} after {attempt + 1} attempt(s).", status, body, retryAfter),
                    _ => new TypeSafeException($"TypeSafe returned HTTP {status}: {Truncate(body)}", status, body),
                };
            }
        }
    }

    private static bool IsRetryable(int status) => status is 429 or 529 or 502 or 503 or 504;

    private TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } ra && ra > TimeSpan.Zero)
        {
            return ra < _options.MaxRetryDelay ? ra : _options.MaxRetryDelay;
        }

        // 200ms, 400ms, 800ms, ... plus up to 100ms jitter, capped.
        var baseMs = 200d * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.NextDouble() * 100d;
        var delay = TimeSpan.FromMilliseconds(baseMs + jitterMs);
        return delay < _options.MaxRetryDelay ? delay : _options.MaxRetryDelay;
    }

    private static string Truncate(string? body, int max = 500)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "<empty body>";
        }

        return body.Length <= max ? body : body[..max] + "…";
    }
}

namespace Kassad.TypeSafe;

/// <summary>Configuration for <see cref="TypeSafeClient"/>.</summary>
public sealed class TypeSafeClientOptions
{
    /// <summary>Default API host.</summary>
    public const string DefaultBaseAddress = "https://api.typesafe.ai/";

    /// <summary>TypeSafe's flagship model alias. Also the SDK default upstream.</summary>
    public const string DefaultModel = "jev-latest";

    /// <summary>Environment variable consulted when <see cref="ApiKey"/> is not set. Matches the official SDKs.</summary>
    public const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>API host. Trailing slash required so relative paths resolve.</summary>
    public Uri BaseAddress { get; set; } = new(DefaultBaseAddress);

    /// <summary>Bearer token. If <c>null</c>, <see cref="ApiKeyEnvironmentVariable"/> is read at construction time.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model route sent in every request.</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>Retries on 429/529 and transient transport failures, after the first attempt. 0 disables retries.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Upper bound on a single backoff delay. <c>Retry-After</c> headers are honored up to this cap.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Per-attempt HTTP timeout applied to the underlying <see cref="HttpClient"/> when registered through DI.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    internal string ResolveApiKey()
    {
        var key = ApiKey ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"No TypeSafe API key. Set {nameof(TypeSafeClientOptions)}.{nameof(ApiKey)} or the {ApiKeyEnvironmentVariable} environment variable.");
        }

        return key;
    }
}

/// <summary>Raised when the TypeSafe API returns an error or an unparseable response.</summary>
public class TypeSafeException : DecisionModelException
{
    /// <summary>HTTP status code, if the failure came from an HTTP response.</summary>
    public int? StatusCode { get; }

    /// <summary>Raw response body, if any. Included for diagnostics; may contain the offending request field on 422.</summary>
    public string? ResponseBody { get; }

    /// <inheritdoc cref="DecisionModelException(string)"/>
    public TypeSafeException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="DecisionModelException(string, Exception)"/>
    public TypeSafeException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Create an exception carrying the HTTP status and body.</summary>
    public TypeSafeException(string message, int statusCode, string? responseBody) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>401. Missing or invalid API key. Never retried.</summary>
public sealed class TypeSafeAuthenticationException : TypeSafeException
{
    /// <inheritdoc cref="TypeSafeException(string, int, string)"/>
    public TypeSafeAuthenticationException(string message, int statusCode, string? responseBody) : base(message, statusCode, responseBody)
    {
    }
}

/// <summary>422. The request body failed validation. Never retried; fix the questions.</summary>
public sealed class TypeSafeRequestException : TypeSafeException
{
    /// <inheritdoc cref="TypeSafeException(string, int, string)"/>
    public TypeSafeRequestException(string message, int statusCode, string? responseBody) : base(message, statusCode, responseBody)
    {
    }
}

/// <summary>429 or 529 after retries were exhausted.</summary>
public sealed class TypeSafeRateLimitException : TypeSafeException
{
    /// <summary>Server-suggested wait, if a <c>Retry-After</c> header was present on the final attempt.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Create a rate-limit exception.</summary>
    public TypeSafeRateLimitException(string message, int statusCode, string? responseBody, TimeSpan? retryAfter) : base(message, statusCode, responseBody)
    {
        RetryAfter = retryAfter;
    }
}

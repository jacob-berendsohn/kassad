namespace Kassad.TypeSafe.Tests;

/// <summary>
/// Shared plumbing for tests that talk to <c>api.typesafe.ai</c>. Live tests carry
/// <c>[Trait("Category", "Live")]</c> so the no-network CI job filters them out, and use
/// <see cref="LiveFactAttribute"/> so they skip rather than fail when no key is present (CONTRIBUTING.md).
/// </summary>
public static class LiveApi
{
    /// <summary>True when <c>TYPESAFE_API_KEY</c> is set in the process environment.</summary>
    public static bool HasApiKey =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TypeSafeClientOptions.ApiKeyEnvironmentVariable));

    /// <summary>A client with default options: real host, <c>jev-latest</c>, key from the environment.</summary>
    public static TypeSafeClient CreateClient(HttpClient http) => new(http, new TypeSafeClientOptions());
}

/// <summary>
/// A fact that needs the live API. Skipped, not failed, when <c>TYPESAFE_API_KEY</c> is absent.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!LiveApi.HasApiKey)
        {
            Skip = $"{TypeSafeClientOptions.ApiKeyEnvironmentVariable} is not set.";
        }
    }
}

/// <summary>
/// Gate for <see cref="FixtureRecorder"/>. Recording overwrites files in the source tree, so it never
/// runs by accident: it needs both <c>TYPESAFE_API_KEY</c> and <c>KASSAD_RECORD_FIXTURES=1</c>.
/// The <c>live</c> CI job has the key but not the opt-in, so it reports the recorder as skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RecordingFactAttribute : FactAttribute
{
    public const string OptInVariable = "KASSAD_RECORD_FIXTURES";

    public RecordingFactAttribute()
    {
        if (!LiveApi.HasApiKey)
        {
            Skip = $"{TypeSafeClientOptions.ApiKeyEnvironmentVariable} is not set.";
        }
        else if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            Skip = $"Set {OptInVariable}=1 to re-record the fixtures from the live API.";
        }
    }
}

/// <summary>
/// Collection for every test that reads or clears <c>TYPESAFE_API_KEY</c> in the process environment.
/// xunit runs test classes in parallel; sharing this collection serializes them so the test that clears
/// the variable cannot race a live test that is constructing a client from it.
/// </summary>
[CollectionDefinition(Name)]
public static class ApiKeyEnvironmentCollection
{
    public const string Name = "TYPESAFE_API_KEY environment";
}

using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kassad.TypeSafe.Tests;

/// <summary>
/// Re-records <c>Fixtures/*.json</c> from the live API. Each file is the raw response body, re-indented,
/// with <c>recorded_at</c>, <c>recorded_status</c> and <c>recorded_request_id</c> prepended; the wire
/// properties follow untouched and in wire order. The requests are defined in <see cref="FixtureRequests"/>.
/// </summary>
/// <remarks>
/// Run it on purpose, never as part of a normal test run:
/// <code>
/// KASSAD_RECORD_FIXTURES=1 TYPESAFE_API_KEY=... dotnet test tests/Kassad.TypeSafe.Tests -f net10.0 --filter FullyQualifiedName~FixtureRecorder
/// </code>
/// then review the diff (model string, token counts, answers) and commit. Without the opt-in variable the
/// test reports itself as skipped, which is what the <c>live</c> CI job sees.
/// </remarks>
[Trait("Category", "Live")]
[Collection(ApiKeyEnvironmentCollection.Name)]
public sealed class FixtureRecorder
{
    private const string SourceDirectoryKey = "Kassad.FixturesSourceDirectory";

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep quotes and non-ASCII as the wire sent them
    };

    [RecordingFact]
    public async Task Record_every_fixture()
    {
        var directory = typeof(FixtureRecorder).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == SourceDirectoryKey)
            .Value!;
        Assert.True(Directory.Exists(directory), $"Fixtures source directory not found: {directory}");

        var options = new TypeSafeClientOptions();
        using var http = new HttpClient { BaseAddress = options.BaseAddress };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ResolveApiKey());

        foreach (var (file, request, expectedStatus) in FixtureRequests.All)
        {
            await RecordAsync(http, options.Model, Path.Combine(directory, file), request, expectedStatus);
        }
    }

    private static async Task RecordAsync(HttpClient http, string model, string path, DecisionRequest request, int expectedStatus)
    {
        using var content = new ByteArrayContent(SystemOneWire.WriteRequest(request, model));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var response = await http.PostAsync(new Uri("v1/systemone", UriKind.Relative), content);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(status == expectedStatus, $"{Path.GetFileName(path)}: expected HTTP {expectedStatus}, got {status}: {body}");

        var file = new JsonObject
        {
            ["recorded_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["recorded_status"] = status,
        };
        if (response.Headers.TryGetValues("x-typesafe-request-id", out var ids) && ids.FirstOrDefault() is { } requestId)
        {
            file["recorded_request_id"] = requestId;
        }

        var wire = JsonNode.Parse(body) as JsonObject
                   ?? throw new InvalidOperationException($"{Path.GetFileName(path)}: response body is not a JSON object: {body}");
        foreach (var (name, value) in wire)
        {
            file[name] = value?.DeepClone();
        }

        var text = file.ToJsonString(FileOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        await File.WriteAllTextAsync(path, text);
    }
}

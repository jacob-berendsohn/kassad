using System.CommandLine;
using System.Text.Json;
using Kassad.Tests;

namespace Kassad.Eval.Tests;

public sealed class CliTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly StringWriter _stdout = new();
    private readonly StringWriter _stderr = new();

    public void Dispose()
    {
        _dir.Dispose();
        _stdout.Dispose();
        _stderr.Dispose();
    }

    private static FakeDecisionModel Answering() => new FakeDecisionModel()
        .Answer("prompt_injection", new NoulAnswer(0.05))
        .Answer("request_class", new ChoiceAnswer(
            "general",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["support"] = 0.1, ["general"] = 0.8, ["prohibited"] = 0.1 },
            0.7));

    private Task<int> InvokeAsync(FakeDecisionModel model, params string[] args) =>
        Cli.Build(model, _stdout, _stderr).Parse(args).InvokeAsync(new InvocationConfiguration { Output = _stdout, Error = _stderr });

    [Fact]
    public async Task Run_requires_dataset_policies_and_out()
    {
        var exit = await InvokeAsync(Answering(), "run");

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("--dataset", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("--policies", _stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("--out", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_rejects_an_unknown_dataset()
    {
        var exit = await InvokeAsync(Answering(), "run", "--dataset", "nope", "--policies", "p.json", "--out", "o.json");

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("deepset", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--concurrency", "0", "--concurrency must be at least 1.")]
    [InlineData("--sample", "0", "--sample must be at least 1.")]
    public async Task Run_rejects_a_count_below_one(string option, string value, string message)
    {
        var exit = await InvokeAsync(Answering(), "run", "--dataset", "deepset", "--policies", "p.json", "--out", "o.json", option, value);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains(message, _stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_is_not_implemented_yet()
    {
        var exit = await InvokeAsync(Answering(), "report", "--in", _dir.Root);

        Assert.Equal(ExitCodes.NotImplemented, exit);
        Assert.Contains("not implemented", _stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_evaluates_the_downloaded_deepset_pages_end_to_end()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var policies = _dir.WritePolicies(TestPolicyFiles.InboundAndOutbound);
        var output = _dir.OutPath("2026-09-18-deepset.json");

        var exit = await InvokeAsync(Answering(), "run", "--dataset", "deepset", "--policies", policies, "--out", output, "--data-dir", _dir.Root, "--concurrency", "2");

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.True(File.Exists(output), _stderr.ToString());
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        var root = document.RootElement;
        Assert.Equal("deepset", root.GetProperty("dataset").GetProperty("name").GetString());
        Assert.Equal("4f61ecb038e9c3fb77e21034b22511b523772cdd", root.GetProperty("dataset").GetProperty("revision").GetString());
        Assert.Equal("apache-2.0", root.GetProperty("dataset").GetProperty("license").GetString());
        Assert.Equal("injection", root.GetProperty("dataset").GetProperty("positive_label").GetString());
        Assert.Equal(2, root.GetProperty("run").GetProperty("concurrency").GetInt32());
        Assert.Equal(["train/0", "train/1", "train/2", "test/0", "test/1"], root.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.All(root.GetProperty("rows").EnumerateArray(), r => Assert.Equal("allow", r.GetProperty("outcome").GetString()));
        Assert.Contains("wrote " + output, _stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_without_the_download_exits_1_and_names_the_script()
    {
        var policies = _dir.WritePolicies(TestPolicyFiles.InboundAndOutbound);
        var output = _dir.OutPath();

        var exit = await InvokeAsync(Answering(), "run", "--dataset", "deepset", "--policies", policies, "--out", output, "--data-dir", _dir.Root);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("eval/datasets/deepset-prompt-injections.sh", _stderr.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Run_with_an_unusable_policy_file_exits_1()
    {
        _dir.WithDeepset(DeepsetRows.Both);
        var policies = _dir.WritePolicies(TestPolicyFiles.OutboundOnly);
        var output = _dir.OutPath();

        var exit = await InvokeAsync(Answering(), "run", "--dataset", "deepset", "--policies", policies, "--out", output, "--data-dir", _dir.Root);

        Assert.Equal(ExitCodes.Fatal, exit);
        Assert.Contains("no inbound policies", _stderr.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(output));
    }
}

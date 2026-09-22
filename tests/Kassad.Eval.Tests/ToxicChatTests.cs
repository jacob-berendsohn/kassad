using System.Text.Json;
using Kassad.Eval.Datasets;

namespace Kassad.Eval.Tests;

public sealed class ToxicChatTests : IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly TempDir _dir = new();
    private readonly ToxicChat _dataset = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Describes_itself_as_the_inbound_toxicity_set()
    {
        Assert.Equal("toxicchat", _dataset.Name);
        Assert.Equal(Stage.Inbound, _dataset.Stage);
        Assert.Equal("toxic", _dataset.PositiveLabel);
        Assert.Equal("{ user_message }", _dataset.StateShape);
        Assert.Equal("eval/datasets/toxicchat.sh", _dataset.DownloadScript);
        Assert.StartsWith("https://huggingface.co/datasets/lmsys/toxic-chat", _dataset.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Loads_train_then_test_with_toxicity_as_the_label_and_the_jailbreaking_flag_ignored()
    {
        _dir.WithToxicChat(ToxicChatRows.Both);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Equal(["train/0", "train/1", "train/2", "test/0", "test/1"], loaded.Rows.Select(r => r.Id));
        Assert.Equal([0, 0, 1, 0, 1], loaded.Rows.Select(r => r.Label));
        Assert.Equal(ToxicChatRows.Train.Select(r => r.Text).Concat(ToxicChatRows.Test.Select(r => r.Text)), loaded.Rows.Select(r => r.Text));
        Assert.All(loaded.Rows, r => Assert.Null(r.SourcePassage));
        Assert.Equal(["train", "test"], loaded.Provenance.Splits);
    }

    [Fact]
    public async Task Reads_provenance_from_the_manifest_and_the_repository_record()
    {
        _dir.WithToxicChat(ToxicChatRows.Both);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Equal("29df8e4dba60e1f4af4b4075c0705c5b313548a8", loaded.Provenance.Revision);
        Assert.Equal("cc-by-nc-4.0", loaded.Provenance.License);
        Assert.Equal(new DateTime(2026, 9, 22, 19, 30, 0, DateTimeKind.Utc), loaded.Provenance.DownloadedAt);
    }

    [Fact]
    public async Task A_missing_download_names_the_script()
    {
        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("eval/datasets/toxicchat.sh", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ToxicChat.DirectoryName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_truncated_reply_is_accepted_because_only_the_prompt_and_the_label_are_read()
    {
        _dir.WithToxicChat(ToxicChatRows.Both, truncatedCellsOfFirstRow: ["model_output", "openai_moderation"]);

        var loaded = await _dataset.LoadAsync(_dir.Root, CancellationToken.None);

        Assert.Equal(5, loaded.Rows.Count);
    }

    [Fact]
    public async Task A_truncated_prompt_is_an_error()
    {
        _dir.WithToxicChat(ToxicChatRows.Both, truncatedCellsOfFirstRow: ["model_output", "user_input"]);

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("row 0 has truncated cells (user_input)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("eval/datasets/toxicchat.sh", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_toxicity_outside_zero_and_one_is_an_error()
    {
        _dir.WithToxicChat(new Dictionary<string, (string Text, int Toxicity, int Jailbreaking)[]>(StringComparer.Ordinal)
        {
            ["train"] = [("fine", 0, 0), ("odd", 2, 0)],
            ["test"] = ToxicChatRows.Test,
        });

        var ex = await Assert.ThrowsAsync<EvalUsageException>(() => _dataset.LoadAsync(_dir.Root, CancellationToken.None));

        Assert.Contains("row 1 has toxicity 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_state_is_the_user_message_field()
    {
        var state = _dataset.BuildState(new EvalRow("test", 0, "hello", 0));

        var json = JsonSerializer.Serialize(state, state.GetType(), WebOptions);
        Assert.Equal("""{"user_message":"hello"}""", json);
    }
}

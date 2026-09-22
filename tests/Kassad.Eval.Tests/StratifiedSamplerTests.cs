using Kassad.Eval.Datasets;
using Kassad.Eval.Running;

namespace Kassad.Eval.Tests;

public class StratifiedSamplerTests
{
    /// <summary><paramref name="count"/> rows with exactly <paramref name="positives"/> labeled 1, spread evenly so order matters.</summary>
    private static EvalRow[] Rows(int count, int positives) =>
        Enumerable.Range(0, count).Select(i => new EvalRow("train", i, $"row {i}", (((i + 1) * positives) / count) - ((i * positives) / count))).ToArray();

    [Fact]
    public void Keeps_the_label_proportions()
    {
        var rows = Rows(100, 30);
        Assert.Equal(30, rows.Count(r => r.Label == 1));

        var sample = StratifiedSampler.Sample(rows, 10, seed: 0);

        Assert.Equal(10, sample.Count);
        Assert.Equal(3, sample.Count(r => r.Label == 1));
    }

    [Fact]
    public void Gives_the_leftover_share_to_the_largest_remainder()
    {
        // 5 negatives and 2 positives; 3 of 7 → quotas 2.14 and 0.86; the positive's remainder wins the leftover seat.
        var rows = new[] { 0, 0, 1, 0, 0, 1, 0 }.Select((label, i) => new EvalRow("train", i, $"row {i}", label)).ToArray();

        var sample = StratifiedSampler.Sample(rows, 3, seed: 0);

        Assert.Equal(2, sample.Count(r => r.Label == 0));
        Assert.Equal(1, sample.Count(r => r.Label == 1));
    }

    [Fact]
    public void Is_deterministic_for_a_seed_and_changes_with_it()
    {
        var rows = Rows(100, 30);

        var first = StratifiedSampler.Sample(rows, 10, seed: 7).Select(r => r.Id).ToArray();
        var again = StratifiedSampler.Sample(rows, 10, seed: 7).Select(r => r.Id).ToArray();
        var other = StratifiedSampler.Sample(rows, 10, seed: 8).Select(r => r.Id).ToArray();

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Keeps_the_dataset_order()
    {
        var sample = StratifiedSampler.Sample(Rows(100, 30), 25, seed: 3);

        Assert.Equal(sample.Select(r => r.Index).Order(), sample.Select(r => r.Index));
        Assert.Equal(25, sample.Select(r => r.Index).Distinct().Count());
    }

    [Fact]
    public void Returns_the_set_itself_when_the_size_covers_it()
    {
        var rows = Rows(10, 3);

        Assert.Same(rows, StratifiedSampler.Sample(rows, 10, seed: 0));
        Assert.Same(rows, StratifiedSampler.Sample(rows, 500, seed: 0));
    }

    [Fact]
    public void Rejects_a_size_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StratifiedSampler.Sample(Rows(10, 3), 0, seed: 0));
    }
}

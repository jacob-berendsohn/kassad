using Kassad.Eval.Reporting;

namespace Kassad.Eval.Tests;

public sealed class MetricsTests
{
    private static Scored[] Rows(params (double Value, int Label)[] rows) => rows.Select(r => new Scored(r.Value, r.Label)).ToArray();

    [Fact]
    public void Auc_is_one_for_a_perfect_ranking_and_zero_for_a_reversed_one()
    {
        Assert.Equal(1.0, Metrics.RocAuc(Rows((0.9, 1), (0.8, 1), (0.2, 0), (0.1, 0))));
        Assert.Equal(0.0, Metrics.RocAuc(Rows((0.1, 1), (0.2, 1), (0.8, 0), (0.9, 0))));
    }

    [Fact]
    public void Auc_counts_ties_as_one_half()
    {
        // Every score tied: each positive-negative pair counts 1/2.
        Assert.Equal(0.5, Metrics.RocAuc(Rows((0.3, 1), (0.3, 0), (0.3, 0), (0.3, 1))));

        // Positive .5 beats one negative and ties the other: (1 + 0.5) / 2.
        Assert.Equal(0.75, Metrics.RocAuc(Rows((0.5, 1), (0.5, 0), (0.1, 0))));
    }

    [Fact]
    public void Auc_is_undefined_without_both_classes()
    {
        Assert.Null(Metrics.RocAuc(Rows((0.9, 1), (0.1, 1))));
        Assert.Null(Metrics.RocAuc(Rows((0.9, 0))));
        Assert.Null(Metrics.RocAuc([]));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.09, 0)]
    [InlineData(0.1, 1)]
    [InlineData(0.3, 3)]
    [InlineData(0.7, 7)]
    [InlineData(0.29, 2)]
    [InlineData(0.99, 9)]
    [InlineData(1.0, 9)]
    [InlineData(-0.1, 0)]
    [InlineData(1.2, 9)]
    public void Calibration_bins_are_half_open_in_decimal_and_the_last_is_closed(double value, int bin)
    {
        Assert.Equal(bin, Metrics.BinOf(value));
    }

    [Fact]
    public void Calibration_reports_mean_predicted_and_observed_rate_per_bin()
    {
        var bins = Metrics.Calibration(Rows((0.05, 0), (0.15, 1), (0.25, 0), (0.95, 1), (0.91, 1), (1.0, 0)));

        Assert.Equal(10, bins.Count);
        Assert.Equal([1, 1, 1, 0, 0, 0, 0, 0, 0, 3], bins.Select(b => b.Rows));
        Assert.Equal(0.0, bins[0].ObservedRate);
        Assert.Equal(1.0, bins[1].ObservedRate);
        Assert.Null(bins[3].MeanPredicted);
        Assert.Null(bins[3].ObservedRate);
        Assert.Equal((0.95 + 0.91 + 1.0) / 3, bins[9].MeanPredicted!.Value, 12);
        Assert.Equal(2.0 / 3, bins[9].ObservedRate!.Value, 12);
        Assert.Equal((0.9, 1.0), (bins[9].Lower, bins[9].Upper));
    }

    [Fact]
    public void Precision_is_undefined_when_nothing_is_predicted_positive()
    {
        var none = new Confusion(0, 0, 3, 5);

        Assert.Null(none.Precision);
        Assert.Equal(0.0, none.Recall);
        Assert.Equal(0.0, none.F1);
    }

    [Fact]
    public void Rates_are_undefined_without_positives_or_predictions()
    {
        var empty = new Confusion(0, 0, 0, 4);

        Assert.Null(empty.Precision);
        Assert.Null(empty.Recall);
        Assert.Null(empty.F1);
    }

    [Fact]
    public void F1_is_the_harmonic_mean_of_precision_and_recall()
    {
        var c = new Confusion(3, 1, 2, 4);

        Assert.Equal(0.75, c.Precision);
        Assert.Equal(0.6, c.Recall);
        Assert.Equal(2 * 0.75 * 0.6 / (0.75 + 0.6), c.F1!.Value, 12);
    }

    [Fact]
    public void Count_splits_rows_by_prediction_and_label()
    {
        var c = Metrics.Count(Rows((0.9, 1), (0.8, 0), (0.2, 1), (0.1, 0), (0.5, 1)), r => r.Value >= 0.5, r => r.Label);

        Assert.Equal(new Confusion(2, 1, 1, 1), c);
    }

    [Theory]
    [InlineData(0.50, 3.0)]
    [InlineData(0.95, 5.0)]
    [InlineData(0.20, 1.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 5.0)]
    public void Nearest_rank_percentile(double p, double expected)
    {
        Assert.Equal(expected, Metrics.NearestRank([1.0, 2.0, 3.0, 4.0, 5.0], p));
    }

    [Fact]
    public void Nearest_rank_of_nothing_is_undefined()
    {
        Assert.Null(Metrics.NearestRank([], 0.5));
    }
}

namespace Kassad.Eval.Reporting;

/// <summary>
/// The statistics <c>kassad-eval report</c> publishes, as pure functions over (scalar, label) pairs. Label 1 is the
/// positive class. Every definition here is stated in the report's footer and in <c>eval/README.md</c>; changing one
/// changes published numbers.
/// </summary>
internal static class Metrics
{
    /// <summary>Number of equal-width calibration bins over [0, 1].</summary>
    public const int CalibrationBinCount = 10;

    /// <summary>
    /// ROC AUC as the Mann–Whitney statistic: the probability that a random positive scores above a random negative,
    /// ties counting one half. Computed from average ranks, so tied scores (the API rounds to two decimals) are handled
    /// exactly. <c>null</c> when either class is absent.
    /// </summary>
    public static double? RocAuc(IReadOnlyList<Scored> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var positives = rows.Count(r => r.Label == 1);
        var negatives = rows.Count - positives;
        if (positives == 0 || negatives == 0)
        {
            return null;
        }

        var sorted = rows.OrderBy(r => r.Value).ToArray();
        var positiveRankSum = 0.0;
        var i = 0;
        while (i < sorted.Length)
        {
            var j = i;
            while (j + 1 < sorted.Length && sorted[j + 1].Value == sorted[i].Value)
            {
                j++;
            }

            // Ranks i+1 .. j+1 share their average.
            var averageRank = (i + j + 2) / 2.0;
            for (var k = i; k <= j; k++)
            {
                if (sorted[k].Label == 1)
                {
                    positiveRankSum += averageRank;
                }
            }

            i = j + 1;
        }

        var u = positiveRankSum - (positives * (positives + 1) / 2.0);
        return u / ((double)positives * negatives);
    }

    /// <summary>
    /// Ten equal-width bins over [0, 1]: <c>[0.0, 0.1)</c> through <c>[0.9, 1.0]</c>, the last one closed. Each bin reports
    /// its row count, the mean predicted value and the observed rate of label 1. The bin is chosen in decimal arithmetic,
    /// so a value of exactly 0.7 lands in <c>[0.7, 0.8)</c> rather than wherever its binary rounding would put it. Values
    /// outside [0, 1] go to the nearest end bin.
    /// </summary>
    public static IReadOnlyList<CalibrationBin> Calibration(IReadOnlyList<Scored> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var counts = new int[CalibrationBinCount];
        var predicted = new double[CalibrationBinCount];
        var positives = new int[CalibrationBinCount];
        foreach (var row in rows)
        {
            var bin = BinOf(row.Value);
            counts[bin]++;
            predicted[bin] += row.Value;
            positives[bin] += row.Label;
        }

        var bins = new CalibrationBin[CalibrationBinCount];
        for (var b = 0; b < CalibrationBinCount; b++)
        {
            bins[b] = new CalibrationBin(
                b / (double)CalibrationBinCount,
                (b + 1) / (double)CalibrationBinCount,
                counts[b],
                counts[b] == 0 ? null : predicted[b] / counts[b],
                counts[b] == 0 ? null : positives[b] / (double)counts[b]);
        }

        return bins;
    }

    /// <summary>The bin index of <paramref name="value"/>; see <see cref="Calibration"/>.</summary>
    public static int BinOf(double value)
    {
        if (!(value > 0))
        {
            return 0;
        }

        if (value >= 1)
        {
            return CalibrationBinCount - 1;
        }

        return Math.Min((int)Math.Floor((decimal)value * CalibrationBinCount), CalibrationBinCount - 1);
    }

    /// <summary>Count predictions against labels: a row is predicted positive when <paramref name="predicted"/> says so.</summary>
    public static Confusion Count<T>(IEnumerable<T> rows, Func<T, bool> predicted, Func<T, int> label)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(predicted);
        ArgumentNullException.ThrowIfNull(label);

        int tp = 0, fp = 0, fn = 0, tn = 0;
        foreach (var row in rows)
        {
            var positive = label(row) == 1;
            if (predicted(row))
            {
                if (positive)
                {
                    tp++;
                }
                else
                {
                    fp++;
                }
            }
            else if (positive)
            {
                fn++;
            }
            else
            {
                tn++;
            }
        }

        return new Confusion(tp, fp, fn, tn);
    }

    /// <summary>
    /// Nearest-rank percentile of an ascending array: the smallest value with at least <paramref name="p"/> of the values at
    /// or below it. <c>null</c> when the array is empty. The run summary and the report both use it, so they agree.
    /// </summary>
    public static double? NearestRank(IReadOnlyList<double> sorted, double p)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        if (sorted.Count == 0)
        {
            return null;
        }

        var rank = Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[rank];
    }
}

/// <summary>One row's scalar for a policy and the row's label (1 positive, 0 negative).</summary>
internal readonly record struct Scored(double Value, int Label);

/// <summary>A confusion matrix and the rates derived from it.</summary>
internal readonly record struct Confusion(int TruePositives, int FalsePositives, int FalseNegatives, int TrueNegatives)
{
    /// <summary>TP / (TP + FP); <c>null</c> when nothing was predicted positive.</summary>
    public double? Precision => TruePositives + FalsePositives == 0 ? null : TruePositives / (double)(TruePositives + FalsePositives);

    /// <summary>TP / (TP + FN); <c>null</c> when there are no positives.</summary>
    public double? Recall => TruePositives + FalseNegatives == 0 ? null : TruePositives / (double)(TruePositives + FalseNegatives);

    /// <summary>2TP / (2TP + FP + FN), the harmonic mean of precision and recall where both exist; 0 when TP is 0 and anything was missed or wrongly flagged.</summary>
    public double? F1 => (2 * TruePositives) + FalsePositives + FalseNegatives == 0
        ? null
        : 2.0 * TruePositives / ((2 * TruePositives) + FalsePositives + FalseNegatives);
}

/// <summary>One calibration bin: <c>[Lower, Upper)</c>, closed at 1.0 for the last.</summary>
internal sealed record CalibrationBin(double Lower, double Upper, int Rows, double? MeanPredicted, double? ObservedRate);

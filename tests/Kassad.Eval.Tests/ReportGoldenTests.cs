using Kassad.Eval.Reporting;

namespace Kassad.Eval.Tests;

/// <summary>
/// The roadmap 4.2 golden test: <c>TestData/report-golden/golden-20.json</c> is a 20-row results file whose metrics were
/// computed by hand (below, row by row) and checked against an independent script before the report code produced them.
/// Row 19 is an error row, so every metric is over the other 19: 8 labeled 1, 11 labeled 0. The expected numbers are
/// asserted to three decimals, and the rendered markdown is compared byte for byte with
/// <c>golden-20.expected.md</c>.
/// </summary>
/// <remarks>
/// The rows (index: label, p(yes), choice, p(prohibited), confidence, latency ms, input tokens):
/// <code>
///  0: 1 .99 prohibited .90 .90 150 600    10: 1 .40 prohibited .55 .40 230 600
///  1: 0 .01 general    .00 .95 100 400    11: 0 .10 general    .00 .95 120 400
///  2: 0 .85 prohibited .70 .80 220 600    12: 1 .90 general    .30 .50 260 600
///  3: 1 .05 general    .00 1.0 130 400    13: 0 .30 general    .05 .90 160 400
///  4: 0 .45 general    .15 .20 170 400    14: 0 .02 general    .01 .95 180 400
///  5: 1 .95 prohibited .80 .60 280 600    15: 1 .85 prohibited .60 .75 270 600
///  6: 0 .03 general    .00 .95 110 400    16: 0 .05 general    .02 .95 210 400
///  7: 1 .60 general    .10 .80 200 600    17: 1 .70 general    .20 .25 250 600
///  8: 0 .62 general    .40 .50 190 600    18: 0 .01 general    .00 .95 240 400
///  9: 0 .20 general    .00 .95 140 400    19: 1 model error (529), latency 5000, no tokens
/// </code>
/// p(yes) of the 8 positives: .99 .95 .90 .85 .70 .60 .40 .05; of the 11 negatives: .85 .62 .45 .30 .20 .10 .05 .03 .02 .01 .01.
/// p(prohibited) of the positives: .90 .80 .30 .60 .20 .10 .55 .00; of the negatives: .70 .40 .15 .05 .00 .00 .02 .00 .01 .00 .00.
/// </remarks>
public sealed class ReportGoldenTests
{
    private static readonly string GoldenDir = Path.Combine(AppContext.BaseDirectory, "TestData", "report-golden");

    private static async Task<FileReport> BuildAsync() =>
        ReportBuilder.Build(await ResultsReader.ReadFileAsync(Path.Combine(GoldenDir, "golden-20.json"), CancellationToken.None));

    private static void AssertPoint(OperatingPoint point, double? precision, double? recall, double? f1, int tp, int fp, int fn, int tn)
    {
        Assert.Equal((tp, fp, fn, tn), (point.Counts.TruePositives, point.Counts.FalsePositives, point.Counts.FalseNegatives, point.Counts.TrueNegatives));
        AssertRate(precision, point.Counts.Precision);
        AssertRate(recall, point.Counts.Recall);
        AssertRate(f1, point.Counts.F1);
    }

    private static void AssertRate(double? expected, double? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.Value, actual.Value, 3);
    }

    [Fact]
    public async Task File_level_latency_tokens_and_cost()
    {
        var report = await BuildAsync();

        Assert.Equal(20, report.Rows);
        Assert.Equal(9, report.Positives);
        Assert.Equal(1, report.ErrorRows);

        // The 19 answered latencies are 100, 110, ..., 280 ms. Nearest rank: p50 is the ceil(0.5 * 19) = 10th, 190 ms;
        // p95 is the ceil(0.95 * 19) = 19th, 280 ms. The error row's 5000 ms is left out.
        Assert.Equal(190.0, report.LatencyP50Ms);
        Assert.Equal(280.0, report.LatencyP95Ms);

        // Ten rows at 400 input tokens and nine at 600: 9400 / 19 = 494.737 per check.
        // Cost per 1k checks: 494.737 * 1000 * $0.042 / 1e6 = $0.020779.
        Assert.Equal(494.737, report.MeanInputTokens!.Value, 3);
        Assert.Equal(0.020779, report.CostPer1kChecksUsd!.Value, 6);
    }

    [Fact]
    public async Task Noul_policy_at_the_configured_thresholds_and_the_sweep()
    {
        var policy = (await BuildAsync()).Policies[0];

        Assert.Equal("prompt_injection", policy.Id);
        Assert.Equal((19, 8, 1), (policy.Scored, policy.Positives, policy.Excluded));

        // Configured (flag 0.40, review 0.60, block 0.85), counted from the recorded actions.
        // flag: positives >= .40 are 7 of 8; negatives .85 .62 .45 cross. P 7/10, R 7/8, F1 14/18.
        // review: positives .99-.60, 6; negatives .85 .62. P 6/8, R 6/8, F1 12/16.
        // block: positives .99 .95 .90 .85, 4; negative .85. P 4/5, R 4/8, F1 8/13.
        Assert.Equal(["flag", "review", "block"], policy.Configured.Select(p => p.Level));
        AssertPoint(policy.Configured[0], 0.700, 0.875, 0.778, 7, 3, 1, 8);
        AssertPoint(policy.Configured[1], 0.750, 0.750, 0.750, 6, 2, 2, 9);
        AssertPoint(policy.Configured[2], 0.800, 0.500, 0.615, 4, 1, 4, 10);

        // Sweep p(yes) >= t for t = 0.1 .. 0.9.
        Assert.Equal(9, policy.Sweep.Count);
        AssertPoint(policy.Sweep[0], 0.538, 0.875, 0.667, 7, 6, 1, 5);   // 0.1: 7/13, 14/21
        AssertPoint(policy.Sweep[1], 0.583, 0.875, 0.700, 7, 5, 1, 6);   // 0.2: 7/12, 14/20
        AssertPoint(policy.Sweep[2], 0.636, 0.875, 0.737, 7, 4, 1, 7);   // 0.3: 7/11, 14/19 (the .30 negative counts)
        AssertPoint(policy.Sweep[3], 0.700, 0.875, 0.778, 7, 3, 1, 8);   // 0.4: the .40 positive counts
        AssertPoint(policy.Sweep[4], 0.750, 0.750, 0.750, 6, 2, 2, 9);   // 0.5
        AssertPoint(policy.Sweep[5], 0.750, 0.750, 0.750, 6, 2, 2, 9);   // 0.6: the .60 positive counts
        AssertPoint(policy.Sweep[6], 0.833, 0.625, 0.714, 5, 1, 3, 10);  // 0.7: 5/6, 10/14
        AssertPoint(policy.Sweep[7], 0.800, 0.500, 0.615, 4, 1, 4, 10);  // 0.8
        AssertPoint(policy.Sweep[8], 1.000, 0.375, 0.545, 3, 0, 5, 11);  // 0.9: 6/11
        Assert.Equal("p(yes) >= 0.90", policy.Sweep[8].Rule);
    }

    [Fact]
    public async Task Noul_policy_roc_auc_and_calibration()
    {
        var policy = (await BuildAsync()).Policies[0];

        // Negatives below each positive (ties half): .99 11, .95 11, .90 11, .85 10.5 (ties the .85 negative),
        // .70 10, .60 9, .40 8, .05 4.5 (ties the .05 negative) = 75 of 8 * 11 = 88 pairs.
        Assert.Equal(75.0 / 88, policy.RocAuc!.Value, 3);
        Assert.Equal(0.852, policy.RocAuc!.Value, 3);

        var bins = policy.Calibration!;
        Assert.Equal(10, bins.Count);
        AssertBin(bins[0], 6, 0.028, 0.167);   // .05+ .05 .03 .02 .01 .01 = .17 / 6; one positive
        AssertBin(bins[1], 1, 0.100, 0.000);
        AssertBin(bins[2], 1, 0.200, 0.000);
        AssertBin(bins[3], 1, 0.300, 0.000);
        AssertBin(bins[4], 2, 0.425, 0.500);   // .40+ .45
        AssertBin(bins[5], 0, null, null);
        AssertBin(bins[6], 2, 0.610, 0.500);   // .60+ .62
        AssertBin(bins[7], 1, 0.700, 1.000);   // .70+ lands in [0.7, 0.8), not below it
        AssertBin(bins[8], 2, 0.850, 0.500);   // .85+ .85
        AssertBin(bins[9], 3, 0.947, 1.000);   // .99 .95 .90, 2.84 / 3
    }

    [Fact]
    public async Task Choice_policy_is_scored_on_the_escalating_option()
    {
        var policy = (await BuildAsync()).Policies[1];

        Assert.Equal("request_class", policy.Id);
        Assert.Equal("p(prohibited)", policy.ScalarName);

        // Configured: prohibited -> block at confidence >= 0.70, and the 0.30 floor sends low confidence to review.
        // review: rows 0 5 10 15 17 (positives) and 2 4 (negatives) resolved review or block. P 5/7, R 5/8, F1 10/15.
        // block: rows 0 15 and 2. P 2/3, R 2/8, F1 4/11.
        Assert.Equal(["review", "block"], policy.Configured.Select(p => p.Level));
        AssertPoint(policy.Configured[0], 0.714, 0.625, 0.667, 5, 2, 3, 9);
        AssertPoint(policy.Configured[1], 0.667, 0.250, 0.364, 2, 1, 6, 10);
        Assert.Equal("chose prohibited; or confidence < 0.30", policy.Configured[0].Rule);
        Assert.Equal("chose prohibited, confidence >= 0.70", policy.Configured[1].Rule);

        AssertPoint(policy.Sweep[0], 0.700, 0.875, 0.778, 7, 3, 1, 8);   // 0.1
        AssertPoint(policy.Sweep[1], 0.750, 0.750, 0.750, 6, 2, 2, 9);   // 0.2
        AssertPoint(policy.Sweep[2], 0.714, 0.625, 0.667, 5, 2, 3, 9);   // 0.3
        AssertPoint(policy.Sweep[3], 0.667, 0.500, 0.571, 4, 2, 4, 9);   // 0.4: 8/14
        AssertPoint(policy.Sweep[4], 0.800, 0.500, 0.615, 4, 1, 4, 10);  // 0.5
        AssertPoint(policy.Sweep[5], 0.750, 0.375, 0.500, 3, 1, 5, 10);  // 0.6
        AssertPoint(policy.Sweep[6], 0.667, 0.250, 0.364, 2, 1, 6, 10);  // 0.7
        AssertPoint(policy.Sweep[7], 1.000, 0.250, 0.400, 2, 0, 6, 11);  // 0.8
        AssertPoint(policy.Sweep[8], 1.000, 0.125, 0.222, 1, 0, 7, 11);  // 0.9: 2/9

        // Below each positive: .90 11, .80 11, .60 10, .55 10, .30 9, .20 9, .10 8, .00 2.5 (five negatives at .00) = 70.5 / 88.
        Assert.Equal(0.801, policy.RocAuc!.Value, 3);

        var bins = policy.Calibration!;
        AssertBin(bins[0], 9, 0.009, 0.111);   // one positive at .00; negatives .05 .02 .01 and five .00: .08 / 9
        AssertBin(bins[1], 2, 0.125, 0.500);   // .10+ .15
        AssertBin(bins[4], 1, 0.400, 0.000);
        AssertBin(bins[7], 1, 0.700, 0.000);
        AssertBin(bins[9], 1, 0.900, 1.000);
    }

    [Fact]
    public async Task Markdown_matches_the_golden_file_byte_for_byte()
    {
        var expected = await File.ReadAllTextAsync(Path.Combine(GoldenDir, "golden-20.expected.md"));

        var markdown = MarkdownReport.Render([await BuildAsync()]);

        Assert.Equal(expected, markdown);
        Assert.Equal(markdown, MarkdownReport.Render([await BuildAsync()]));
    }

    [Fact]
    public async Task Markdown_tables_are_well_formed_for_GitHub()
    {
        var markdown = MarkdownReport.Render([await BuildAsync()]);
        var lines = markdown.Split('\n');

        Assert.DoesNotContain('\r', markdown);
        Assert.All(markdown, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4}: the report must survive any console code page"));
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);

        // Every table: a blank line before it, a delimiter row right after the header, the same cell count on every row.
        var tables = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith('|') || (i > 0 && lines[i - 1].StartsWith('|')))
            {
                continue;
            }

            tables++;
            Assert.Equal(string.Empty, lines[i - 1]);
            var cells = CellCount(lines[i]);
            Assert.Matches(@"^\|(-{3,}:?\|)+$", lines[i + 1].Replace(":-", "-", StringComparison.Ordinal));
            for (var j = i + 1; j < lines.Length && lines[j].StartsWith('|'); j++)
            {
                Assert.Equal(cells, CellCount(lines[j]));
            }
        }

        Assert.Equal(6, tables); // the summary over files, the file summary, then operating points and calibration for each of the two policies
    }

    private static int CellCount(string row) => row.Replace("\\|", string.Empty, StringComparison.Ordinal).Count(c => c == '|') - 1;

    private static void AssertBin(CalibrationBin bin, int rows, double? meanPredicted, double? observed)
    {
        Assert.Equal(rows, bin.Rows);
        AssertRate(meanPredicted, bin.MeanPredicted);
        AssertRate(observed, bin.ObservedRate);
    }
}

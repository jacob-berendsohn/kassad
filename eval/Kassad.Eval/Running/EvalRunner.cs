using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Kassad.Engine;
using Kassad.Eval.Datasets;
using Kassad.Eval.Results;
using Kassad.Policies;
using Kassad.TypeSafe;

namespace Kassad.Eval.Running;

/// <summary>
/// Runs every policy of the dataset's stage over each row through <see cref="GuardrailEngine"/>, a bounded number of
/// rows at a time, and writes one result row per input. The engine is used rather than the model directly so the
/// verdicts, the batching (one request per row carrying all the stage's policies) and the latency (the engine's
/// <see cref="StageResult.ModelLatency"/>, the same stopwatch the <c>kassad.model.latency</c> histogram records) are
/// the shipped ones. Retries live in <see cref="TypeSafeClient"/>; a failure that survives them becomes error verdicts
/// recorded on the row, and the run goes on. The first row is evaluated alone: a failure there that no retry can fix
/// (a rejected key, a request the API rejects) stops the run before anything is written.
/// </summary>
internal sealed class EvalRunner
{
    private const int ProgressEvery = 25;

    private static readonly string? HarnessVersion =
        typeof(EvalRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private readonly IDecisionModel _model;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;

    /// <param name="model">The decision model; normally a <see cref="TypeSafeClient"/>.</param>
    /// <param name="stdout">Receives the summary when the run ends.</param>
    /// <param name="stderr">Receives progress while it runs.</param>
    public EvalRunner(IDecisionModel model, TextWriter stdout, TextWriter stderr)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _stderr = TextWriter.Synchronized(stderr ?? throw new ArgumentNullException(nameof(stderr)));
    }

    /// <summary>Run and write the results file. Returns the process exit code (<see cref="ExitCodes"/>).</summary>
    /// <exception cref="EvalUsageException">The policies or the dataset could not be loaded, or the first request failed in a way retrying cannot fix. Nothing was written.</exception>
    public async Task<int> RunAsync(RunSettings settings, IEvalDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dataset);

        var policies = LoadPolicies(settings.PoliciesPath);
        var stagePolicies = policies.ForStage(dataset.Stage);
        var stageName = Names.StageName(dataset.Stage);
        if (stagePolicies.IsEmpty)
        {
            throw new EvalUsageException($"{settings.PoliciesPath} has no {stageName} policies, and dataset '{dataset.Name}' evaluates that stage.");
        }

        var byId = stagePolicies.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var loaded = await dataset.LoadAsync(settings.DataDir, cancellationToken).ConfigureAwait(false);
        if (loaded.Rows.Count == 0)
        {
            throw new EvalUsageException($"Dataset '{dataset.Name}' has no rows.");
        }

        var rows = settings.Sample is { } size ? StratifiedSampler.Sample(loaded.Rows, size, settings.Seed) : loaded.Rows;
        var sample = rows.Count < loaded.Rows.Count ? new SampleInfo(rows.Count, settings.Seed, StratifiedSampler.Method) : null;

        var recording = new RecordingDecisionModel(_model);
        var engine = new GuardrailEngine(recording, policies);

        var sampleNote = sample is null ? string.Empty : Invariant($" (stratified sample of {loaded.Rows.Count}, seed {sample.Seed})");
        await _stderr.WriteLineAsync(Invariant(
            $"kassad-eval: {dataset.Name}: {rows.Count} rows{sampleNote}; {stageName} policies {string.Join(", ", byId.Keys)}; model {_model.Name}; concurrency {settings.Concurrency}")).ConfigureAwait(false);

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new RowResult?[rows.Count];
        var completed = 0;
        var errors = 0;
        var interrupted = false;

        // The first row alone: a failure no retry can fix stops the run here instead of writing a file of error rows.
        var first = await EvaluateAsync(engine, dataset, byId, rows[0], cancellationToken).ConfigureAwait(false);
        if (first.Error is not null && recording.LastFailure is TypeSafeAuthenticationException or TypeSafeRequestException)
        {
            throw new EvalUsageException($"the first request failed in a way retrying cannot fix, so nothing was written: {recording.LastFailure.Message}");
        }

        results[0] = first;
        completed = 1;
        errors = first.Error is null ? 0 : 1;

        try
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = settings.Concurrency, CancellationToken = cancellationToken };
            await Parallel.ForEachAsync(Enumerable.Range(1, rows.Count - 1), options, async (i, ct) =>
            {
                var result = await EvaluateAsync(engine, dataset, byId, rows[i], ct).ConfigureAwait(false);
                results[i] = result;
                if (result.Error is not null)
                {
                    Interlocked.Increment(ref errors);
                }

                var done = Interlocked.Increment(ref completed);
                if (done % ProgressEvery == 0 || done == rows.Count)
                {
                    await ReportProgressAsync(done, rows.Count, Volatile.Read(ref errors), stopwatch.Elapsed).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            interrupted = true;
        }

        stopwatch.Stop();
        var finishedAt = DateTime.UtcNow;

        var evaluated = results.Where(r => r is not null).Select(r => r!).ToArray();
        var errorCount = evaluated.Count(r => r.Error is not null);
        var outcomes = Names.AllActions.ToDictionary(a => a, a => evaluated.Count(r => r.Outcome == a), StringComparer.Ordinal);

        var runResults = new RunResults
        {
            Harness = new HarnessInfo("kassad-eval", HarnessVersion),
            Run = new RunInfo(startedAt, finishedAt, stopwatch.ElapsedMilliseconds, settings.Concurrency, interrupted),
            Dataset = new DatasetInfo(
                dataset.Name,
                dataset.Source,
                loaded.Provenance.Revision,
                loaded.Provenance.License,
                loaded.Provenance.DownloadedAt,
                loaded.Provenance.Splits,
                loaded.Rows.Count,
                evaluated.Length,
                sample,
                dataset.PositiveLabel,
                stageName,
                dataset.StateShape),
            Policies = new PoliciesInfo(
                settings.PoliciesPath.Replace('\\', '/'),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(settings.PoliciesPath))),
                stageName,
                stagePolicies.Select(PolicyInfo.From).ToArray()),
            Model = new ModelInfo(_model.Name, recording.ResolvedModels),
            Summary = new RunSummary(
                evaluated.Length,
                errorCount,
                outcomes,
                evaluated.Sum(r => (long)(r.InputTokens ?? 0)),
                evaluated.Sum(r => (long)(r.OutputTokens ?? 0))),
            Rows = evaluated,
        };

        // Written even after an interruption, marked as such, so a long run is not lost to a Ctrl+C.
        await ResultsWriter.WriteAsync(settings.OutPath, runResults, CancellationToken.None).ConfigureAwait(false);
        await PrintSummaryAsync(runResults, settings.OutPath).ConfigureAwait(false);

        return interrupted ? ExitCodes.Interrupted
            : errorCount > 0 ? ExitCodes.CompletedWithErrors
            : ExitCodes.Ok;
    }

    private static async Task<RowResult> EvaluateAsync(
        GuardrailEngine engine,
        IEvalDataset dataset,
        IReadOnlyDictionary<string, Policy> policies,
        EvalRow row,
        CancellationToken cancellationToken)
    {
        var result = await engine.EvaluateAsync(dataset.Stage, dataset.BuildState(row), cancellationToken).ConfigureAwait(false);
        return RowResult.From(row, result, policies);
    }

    private static PolicySet LoadPolicies(string path)
    {
        try
        {
            return PolicySet.FromFile(path);
        }
        catch (PolicyValidationException ex)
        {
            throw new EvalUsageException($"{path}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvalUsageException($"{path}: {ex.Message}", ex);
        }
    }

    private Task ReportProgressAsync(int done, int total, int errors, TimeSpan elapsed) =>
        _stderr.WriteLineAsync(Invariant($"kassad-eval: {done}/{total} rows, {errors} errors, {elapsed.TotalSeconds:0.0} s"));

    private async Task PrintSummaryAsync(RunResults results, string outPath)
    {
        var latencies = results.Rows.Where(r => r.Error is null).Select(r => r.LatencyMs).Order().ToArray();
        var coverage = results.Dataset.Sample is { } sample
            ? Invariant($"a {sample.Size}-row sample of {results.Dataset.RowsTotal}")
            : Invariant($"the full set of {results.Dataset.RowsTotal}");
        var interrupted = results.Run.Interrupted ? ", interrupted" : string.Empty;
        var resolved = results.Model.Resolved.Count == 0 ? "no answer" : string.Join(", ", results.Model.Resolved);

        await _stdout.WriteLineAsync(Invariant(
            $"kassad-eval run: {results.Dataset.Name}: {results.Summary.Rows} rows evaluated over {coverage}{interrupted}; {results.Summary.Errors} errors; {results.Run.DurationMs / 1000.0:0.0} s at concurrency {results.Run.Concurrency}")).ConfigureAwait(false);
        await _stdout.WriteLineAsync(Invariant(
            $"  outcomes: {string.Join(", ", results.Summary.Outcomes.Select(kv => Invariant($"{kv.Key} {kv.Value}")))}")).ConfigureAwait(false);
        await _stdout.WriteLineAsync(Invariant(
            $"  model {results.Model.Name} (answered by {resolved}): latency p50 {Percentile(latencies, 0.50)}, p95 {Percentile(latencies, 0.95)} over {latencies.Length} calls; {results.Summary.InputTokens} input / {results.Summary.OutputTokens} output tokens")).ConfigureAwait(false);
        await _stdout.WriteLineAsync($"  wrote {outPath}").ConfigureAwait(false);
    }

    /// <summary>Nearest-rank percentile of a sorted array, formatted; <c>n/a</c> when there is nothing to rank.</summary>
    private static string Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return "n/a";
        }

        var rank = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
        return Invariant($"{sorted[rank]:0.0} ms");
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}

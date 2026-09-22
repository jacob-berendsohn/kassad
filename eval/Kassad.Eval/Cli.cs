using System.CommandLine;
using Kassad.Eval.Datasets;
using Kassad.Eval.Reporting;
using Kassad.Eval.Running;
using Kassad.TypeSafe;

namespace Kassad.Eval;

/// <summary>
/// The <c>kassad-eval</c> command line. <c>run</c> evaluates a policy file over a labeled dataset and writes one row
/// per input (roadmap 4.1); <c>report</c> turns results files into the README numbers (roadmap 4.2).
/// Built with a real <see cref="TypeSafeClient"/> by default; tests hand in a model and writers of their own.
/// </summary>
internal static class Cli
{
    /// <summary>Build the root command. <paramref name="model"/> replaces the TypeSafe client when given.</summary>
    public static RootCommand Build(IDecisionModel? model = null, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var root = new RootCommand("Kassad evaluation harness: runs policies over labeled datasets and reports the numbers.");
        root.Add(BuildRun(model, stdout, stderr));
        root.Add(BuildReport(stdout, stderr));
        return root;
    }

    private static Command BuildRun(IDecisionModel? model, TextWriter stdout, TextWriter stderr)
    {
        var dataset = new Option<string>("--dataset")
        {
            Description = $"Dataset to evaluate ({string.Join(", ", EvalDatasets.Names)}). Download it first with its script under eval/datasets/.",
            Required = true,
        };
        dataset.AcceptOnlyFromAmong(EvalDatasets.Names.ToArray());

        var policies = new Option<FileInfo>("--policies")
        {
            Description = "Policy file (Docs/specs/policy-file-format.md). Only the dataset's stage is evaluated.",
            Required = true,
        };

        var output = new Option<FileInfo>("--out")
        {
            Description = "Results file to write (eval/README.md documents its schema). Replaced if it exists.",
            Required = true,
        };

        var dataDir = new Option<DirectoryInfo>("--data-dir")
        {
            Description = "Directory the download scripts fill.",
            DefaultValueFactory = _ => new DirectoryInfo(RunSettings.DefaultDataDir),
        };

        var concurrency = new Option<int>("--concurrency")
        {
            Description = "Rows evaluated at the same time.",
            DefaultValueFactory = _ => RunSettings.DefaultConcurrency,
        };
        concurrency.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
            {
                result.AddError("--concurrency must be at least 1.");
            }
        });

        var sample = new Option<int?>("--sample")
        {
            Description = "Evaluate a deterministic stratified sample of this many rows instead of the full set; the results file records it.",
        };
        sample.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is < 1)
            {
                result.AddError("--sample must be at least 1.");
            }
        });

        var seed = new Option<int>("--seed")
        {
            Description = "Seed for --sample.",
            DefaultValueFactory = _ => RunSettings.DefaultSeed,
        };

        var modelName = new Option<string>("--model")
        {
            Description = "TypeSafe model alias to send.",
            DefaultValueFactory = _ => TypeSafeClientOptions.DefaultModel,
        };

        var run = new Command("run", "Evaluate a policy file over a labeled dataset and write one result row per input.")
        {
            dataset,
            policies,
            output,
            dataDir,
            concurrency,
            sample,
            seed,
            modelName,
        };

        run.SetAction((parseResult, cancellationToken) =>
        {
            var settings = new RunSettings
            {
                DatasetName = parseResult.GetRequiredValue(dataset),
                PoliciesPath = parseResult.GetRequiredValue(policies).ToString(),
                OutPath = parseResult.GetRequiredValue(output).ToString(),
                DataDir = parseResult.GetRequiredValue(dataDir).ToString(),
                Concurrency = parseResult.GetRequiredValue(concurrency),
                Sample = parseResult.GetValue(sample),
                Seed = parseResult.GetRequiredValue(seed),
                Model = parseResult.GetRequiredValue(modelName),
            };

            return RunAsync(settings, model, stdout, stderr, cancellationToken);
        });

        return run;
    }

    private static Command BuildReport(TextWriter stdout, TextWriter stderr)
    {
        var input = new Option<DirectoryInfo>("--in")
        {
            Description = "Directory of results files written by run; every *.json directly in it is reported, in file-name order.",
            Required = true,
        };

        var format = new Option<string>("--format")
        {
            Description = "Output format.",
            DefaultValueFactory = _ => "markdown",
        };
        format.AcceptOnlyFromAmong("markdown");

        var report = new Command("report", "Turn results files into the README numbers: precision, recall and F1 per threshold, ROC AUC, calibration, latency and cost.")
        {
            input,
            format,
        };

        report.SetAction((parseResult, cancellationToken) =>
            ReportAsync(parseResult.GetRequiredValue(input).ToString(), stdout, stderr, cancellationToken));

        return report;
    }

    private static async Task<int> ReportAsync(string directory, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        try
        {
            var documents = await ResultsReader.ReadDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            var markdown = MarkdownReport.Render(documents.Select(ReportBuilder.Build).ToArray());
            await stdout.WriteAsync(markdown).ConfigureAwait(false);
            return ExitCodes.Ok;
        }
        catch (EvalUsageException ex)
        {
            await stderr.WriteLineAsync($"kassad-eval: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.Fatal;
        }
    }

    private static async Task<int> RunAsync(RunSettings settings, IDecisionModel? model, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        HttpClient? http = null;
        try
        {
            var dataset = EvalDatasets.Get(settings.DatasetName);
            if (model is null)
            {
                http = new HttpClient();
                model = CreateTypeSafeClient(settings, http);
            }

            var runner = new EvalRunner(model, stdout, stderr);
            return await runner.RunAsync(settings, dataset, cancellationToken).ConfigureAwait(false);
        }
        catch (EvalUsageException ex)
        {
            await stderr.WriteLineAsync($"kassad-eval: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.Fatal;
        }
        finally
        {
            http?.Dispose();
        }
    }

    /// <summary>The real model: <see cref="TypeSafeClient"/> over one <see cref="HttpClient"/>, key from <c>TYPESAFE_API_KEY</c>, eval-length retries.</summary>
    private static TypeSafeClient CreateTypeSafeClient(RunSettings settings, HttpClient http)
    {
        var options = new TypeSafeClientOptions
        {
            Model = settings.Model,
            MaxRetries = RunSettings.MaxRetries,
            MaxRetryDelay = RunSettings.MaxRetryDelay,
        };
        http.Timeout = options.Timeout;

        try
        {
            return new TypeSafeClient(http, options);
        }
        catch (InvalidOperationException ex)
        {
            // No key in the environment; the message names the variable.
            throw new EvalUsageException(ex.Message, ex);
        }
    }
}

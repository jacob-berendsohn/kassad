// Evaluation harness: roadmap Phase 4.
//
// Contract (do not change without updating Docs/roadmap.md and README "Numbers"):
//   kassad-eval run    --dataset <name> --policies <file> --out results/<date>-<dataset>.json   (roadmap 4.1)
//   kassad-eval report --in results/ --format markdown                                            (roadmap 4.2, not yet built)
//
// Datasets are downloaded to eval/data/ by scripts in eval/datasets/ and never committed; eval/README.md documents
// the JSON that `run` writes, one row per input. Metrics per policy (4.2): precision, recall, F1 at each configured
// threshold; ROC AUC; calibration bins (predicted probability vs observed positive rate); p50/p95 model latency;
// input tokens and cost per 1k checks.

using System.CommandLine;
using Kassad.Eval;

// Ctrl+C cancels the run; the runner then writes the rows it has, which takes longer than the two-second default.
var invocation = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(15) };
return await Cli.Build().Parse(args).InvokeAsync(invocation);

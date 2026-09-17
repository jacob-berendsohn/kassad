// Evaluation harness: roadmap Phase 4.
//
// Contract (do not change without updating Docs/roadmap.md and README "Numbers"):
//   kassad-eval run  --dataset <name> --policies <file> --out results/<date>-<dataset>.json
//   kassad-eval report --in results/ --format markdown
//
// Datasets are downloaded to eval/data/ by scripts in eval/datasets/ and never committed.
// Metrics per policy: precision, recall, F1 at each configured threshold; ROC AUC; calibration bins
// (predicted probability vs observed positive rate); p50/p95 model latency; input tokens and cost per 1k checks.

Console.Error.WriteLine("kassad-eval: not implemented yet. See Docs/roadmap.md, Phase 4.");
return 2;

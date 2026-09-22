### golden: `golden-20.json`

Source: hand-computed fixture (tests/Kassad.Eval.Tests/TestData/report-golden), splits test: the full set of 20 rows, label 1 = injection. Stage `inbound`, state `{ user_message }`, policies from `samples/Kassad.Sample.ChatApi/kassad.policies.json` (SHA-256 `21461a62`). Model `typesafe:jev-latest`, answered by `jev-1.13.0`.

| Rows | Label 1 (injection) | Label 0 | Error rows | Latency p50 | Latency p95 | Input tokens per check | Cost per 1k checks |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 20 | 9 | 11 | 1 | 190.0 ms | 280.0 ms | 494.7 | $0.0208 |

Cost per 1k checks is the mean input tokens per check times 1,000 at $0.042 per 1M input tokens; output tokens are not billed. Rate quoted, verify: [MarkTechPost, 2026-09-19](https://www.marktechpost.com/2026/09/19/typesafe-ai-releases-jev/). A check is one request carrying the file's 2 inbound policies; latency and tokens are per check, over the rows that got an answer.

#### `prompt_injection` (noul, scored on p(yes))

ROC AUC **0.852** over 19 rows (8 label 1, 11 label 0; 1 error row left out).

| Operating point | Rule | Precision | Recall | F1 | TP | FP | FN | TN |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `flag` (configured) | p(yes) >= 0.40 | 0.700 | 0.875 | 0.778 | 7 | 3 | 1 | 8 |
| `review` (configured) | p(yes) >= 0.60 | 0.750 | 0.750 | 0.750 | 6 | 2 | 2 | 9 |
| `block` (configured) | p(yes) >= 0.85 | 0.800 | 0.500 | 0.615 | 4 | 1 | 4 | 10 |
| sweep | p(yes) >= 0.10 | 0.538 | 0.875 | 0.667 | 7 | 6 | 1 | 5 |
| sweep | p(yes) >= 0.20 | 0.583 | 0.875 | 0.700 | 7 | 5 | 1 | 6 |
| sweep | p(yes) >= 0.30 | 0.636 | 0.875 | 0.737 | 7 | 4 | 1 | 7 |
| sweep | p(yes) >= 0.40 | 0.700 | 0.875 | 0.778 | 7 | 3 | 1 | 8 |
| sweep | p(yes) >= 0.50 | 0.750 | 0.750 | 0.750 | 6 | 2 | 2 | 9 |
| sweep | p(yes) >= 0.60 | 0.750 | 0.750 | 0.750 | 6 | 2 | 2 | 9 |
| sweep | p(yes) >= 0.70 | 0.833 | 0.625 | 0.714 | 5 | 1 | 3 | 10 |
| sweep | p(yes) >= 0.80 | 0.800 | 0.500 | 0.615 | 4 | 1 | 4 | 10 |
| sweep | p(yes) >= 0.90 | 1.000 | 0.375 | 0.545 | 3 | 0 | 5 | 11 |

Calibration of p(yes) in 10 equal-width bins:

| p(yes) bin | Rows | Mean predicted | Observed rate |
|---|---:|---:|---:|
| [0.0, 0.1) | 6 | 0.028 | 0.167 |
| [0.1, 0.2) | 1 | 0.100 | 0.000 |
| [0.2, 0.3) | 1 | 0.200 | 0.000 |
| [0.3, 0.4) | 1 | 0.300 | 0.000 |
| [0.4, 0.5) | 2 | 0.425 | 0.500 |
| [0.5, 0.6) | 0 | n/a | n/a |
| [0.6, 0.7) | 2 | 0.610 | 0.500 |
| [0.7, 0.8) | 1 | 0.700 | 1.000 |
| [0.8, 0.9) | 2 | 0.850 | 0.500 |
| [0.9, 1.0] | 3 | 0.947 | 1.000 |

#### `request_class` (choice, scored on p(prohibited))

ROC AUC **0.801** over 19 rows (8 label 1, 11 label 0; 1 error row left out).

| Operating point | Rule | Precision | Recall | F1 | TP | FP | FN | TN |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| `review` (configured) | chose prohibited; or confidence < 0.30 | 0.714 | 0.625 | 0.667 | 5 | 2 | 3 | 9 |
| `block` (configured) | chose prohibited, confidence >= 0.70 | 0.667 | 0.250 | 0.364 | 2 | 1 | 6 | 10 |
| sweep | p(prohibited) >= 0.10 | 0.700 | 0.875 | 0.778 | 7 | 3 | 1 | 8 |
| sweep | p(prohibited) >= 0.20 | 0.750 | 0.750 | 0.750 | 6 | 2 | 2 | 9 |
| sweep | p(prohibited) >= 0.30 | 0.714 | 0.625 | 0.667 | 5 | 2 | 3 | 9 |
| sweep | p(prohibited) >= 0.40 | 0.667 | 0.500 | 0.571 | 4 | 2 | 4 | 9 |
| sweep | p(prohibited) >= 0.50 | 0.800 | 0.500 | 0.615 | 4 | 1 | 4 | 10 |
| sweep | p(prohibited) >= 0.60 | 0.750 | 0.375 | 0.500 | 3 | 1 | 5 | 10 |
| sweep | p(prohibited) >= 0.70 | 0.667 | 0.250 | 0.364 | 2 | 1 | 6 | 10 |
| sweep | p(prohibited) >= 0.80 | 1.000 | 0.250 | 0.400 | 2 | 0 | 6 | 11 |
| sweep | p(prohibited) >= 0.90 | 1.000 | 0.125 | 0.222 | 1 | 0 | 7 | 11 |

Calibration of p(prohibited) in 10 equal-width bins:

| p(prohibited) bin | Rows | Mean predicted | Observed rate |
|---|---:|---:|---:|
| [0.0, 0.1) | 9 | 0.009 | 0.111 |
| [0.1, 0.2) | 2 | 0.125 | 0.500 |
| [0.2, 0.3) | 1 | 0.200 | 1.000 |
| [0.3, 0.4) | 1 | 0.300 | 1.000 |
| [0.4, 0.5) | 1 | 0.400 | 0.000 |
| [0.5, 0.6) | 1 | 0.550 | 1.000 |
| [0.6, 0.7) | 1 | 0.600 | 1.000 |
| [0.7, 0.8) | 1 | 0.700 | 0.000 |
| [0.8, 0.9) | 1 | 0.800 | 1.000 |
| [0.9, 1.0] | 1 | 0.900 | 1.000 |

How these numbers are computed: every policy is scored against the dataset's label, whether or not that label is what the policy asks about. Rows whose model call failed are left out and counted. A configured row counts a row as positive when the action the engine resolved in the run is at or above that level, so confidence floors count as they did in production; a sweep row thresholds the policy's scalar (a noul's p(yes), a choice's summed probability of the options its `actions` escalate, a score's weighted level). Precision is n/a when no row crosses; F1 = 2TP / (2TP + FP + FN). ROC AUC is the Mann-Whitney statistic of the scalar, ties counting one half. Calibration bins are equal-width and half-open, the last one closed; empty bins show n/a. Latency is the model call as the engine timed it (client retries included), nearest-rank percentiles. Generated by `kassad-eval report`; do not edit by hand.

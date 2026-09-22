# Evaluation

A guardrail without published numbers is a toy. This directory holds the harness that produces the
table in the root README.

- `Kassad.Eval/` — the `kassad-eval` console app. `run` (roadmap 4.1) evaluates a policy file over a labeled dataset
  and writes one result row per input; `report` (roadmap 4.2) will turn those files into the numbers.
- `datasets/` — one download script per dataset. Raw data lands in `data/` (git-ignored) and is never committed.
- `results/` — dated JSON written by `run`. Committed, so the README table is reproducible.

## Running an evaluation

Everything below runs from the repository root, with `TYPESAFE_API_KEY` in the environment (the harness reads it the
way the sample does; nothing else is configured).

1. Download the dataset once. The script needs only `curl`:

   ```bash
   eval/datasets/deepset-prompt-injections.sh
   ```

   It fills `eval/data/deepset-prompt-injections/` with JSON pages of rows, a `manifest.json` (source revision, download
   time) and the repository's `dataset-info.json` (license).

2. Run a policy file over it:

   ```bash
   TYPESAFE_API_KEY=... dotnet run --project eval/Kassad.Eval -c Release -- run --dataset deepset --policies samples/Kassad.Sample.ChatApi/kassad.policies.json --out eval/results/2026-09-18-deepset.json
   ```

   The executable is named `kassad-eval` (`eval/Kassad.Eval/bin/Release/net10.0/kassad-eval`), so the roadmap's
   `kassad-eval run ...` is the same command without `dotnet run`.

| Option | Default | Meaning |
|---|---|---|
| `--dataset <name>` | required | Which dataset: `deepset` today; the table below grows with roadmap 4.3. |
| `--policies <file>` | required | The policy file (`Docs/specs/policy-file-format.md`). Only the policies for the dataset's stage are evaluated. |
| `--out <path>` | required | The results file. Replaced if it exists; the directory is created. |
| `--data-dir <dir>` | `eval/data` | Where the download scripts put the raw data. |
| `--concurrency <n>` | `4` | Rows in flight at once. Rate limits are unknown, so the default is conservative; raise it if the run stays error-free. |
| `--sample <n>` | full set | Evaluate a deterministic stratified sample of `n` rows instead of the full set (see below). The file records it. |
| `--seed <n>` | `0` | Seed for `--sample`. |
| `--model <alias>` | `jev-latest` | The TypeSafe model alias to send. The file records which release answered. |

What a run does:

- Loads and validates the policy file with `PolicySet.FromFile`, exactly as `AddKassad` does, and keeps the policies of
  the dataset's stage. A file with none for that stage is an error.
- Sends every row through `GuardrailEngine` over `TypeSafeClient`, so the verdicts are the shipped engine's. Each row's
  text is evaluated as `{ "user_message": "<text>" }`, the state the middleware sends for a chat request without a system
  prompt (`Docs/specs/state-extraction.md`), and one request carries all of the stage's policies, as in production.
- Runs `--concurrency` rows at a time. Retries are the client's (`TypeSafeClient`, set to 5 retries with up to 30 s between
  them, honoring `Retry-After`). A failure that survives them becomes the policies' `on_error` verdicts, recorded on the
  row with `from_error: true` and `error`, and the run continues; it does not retry the row again.
- Evaluates the first row alone. A failure there that no retry can fix (a rejected key, a request the API rejects with
  400 or 422) stops the run before anything is written, instead of producing a file full of error rows.
- Prints progress to stderr every 25 rows and a summary (rows, errors, outcomes, latency p50/p95, tokens) to stdout.
  Ctrl+C writes the rows evaluated so far with `"interrupted": true`.

Exit codes:

| Code | Meaning |
|---|---|
| `0` | Every row was evaluated and the file was written. |
| `1` | Nothing was written: bad arguments (System.CommandLine prints them), a dataset that is not downloaded (the message names the script), an invalid policy file, no key, or a first request the API rejected. |
| `2` | The command is part of the contract but not implemented yet (`report`, until roadmap 4.2). |
| `3` | The run completed and the file was written, but some rows carry a model error instead of an answer (`summary.errors`). |
| `130` | Cancelled; the file holds the rows evaluated so far and says so. |

### Samples

The roadmap's fallback for rate limits is a stratified sample of at least 500 rows per dataset, stated in the report.
`--sample n` draws one: rows are grouped by label, each label gets its proportional share of `n` (largest remainder, so
the shares add up), each group is shuffled with `--seed` and contributes that many rows, and the result keeps the
dataset's order. The same rows, size and seed always give the same sample. The file records `dataset.sample` with the
size, seed and method; it is `null` for a full run.

## Datasets

| Dataset | `--dataset` | Stage | Rows | License | Script | Label 1 means |
|---|---|---|---|---|---|---|
| [deepset/prompt-injections](https://huggingface.co/datasets/deepset/prompt-injections) | `deepset` | inbound | 546 train + 116 test | Apache-2.0 | `datasets/deepset-prompt-injections.sh` | prompt injection (0 = legitimate request) |
| JailbreakBench | roadmap 4.3 | inbound | | | | jailbreak |
| ToxicChat | roadmap 4.3, if the license allows | inbound | | | | unsafe request |
| A grounding set (TBD) | roadmap 4.3 | grounding | | | | unsupported claim |

The deepset repository's canonical files are Parquet, which .NET cannot read without another package. The script
fetches the same rows as JSON pages (100 rows each) from the Hugging Face datasets-server rows API instead, records the
repository revision it saw in `manifest.json`, and the adapter refuses pages with truncated cells, duplicate rows or
fewer rows than the source reports. Both splits are evaluated: nothing is trained here, so the split is only a tag on
each row (`split`), and the file carries the dataset revision so a run can be traced back to the exact rows.

Adding a dataset (roadmap 4.3) is one download script, one `IEvalDataset` class that maps its rows to `(text, label)`
and declares its stage and state shape, and one line in `EvalDatasets`.

## Results file

`run` writes one JSON document per run, schema version `1`: the envelope indented, then `rows` as one compact object
per line, so a diff shows one line per changed row and a ten-thousand-row file stays a few megabytes. Names are
snake_case, line endings LF, no BOM. Dataset text never appears in the file (raw data is never committed); each row
carries the text's hash and length, and `id` joins it back to the source.

Envelope:

| Field | Meaning |
|---|---|
| `schema_version` | `1`. Bumped when a field below is renamed or removed. |
| `harness.name`, `harness.version` | `kassad-eval` and the assembly's informational version (MinVer: version plus commit), or `null`. |
| `run.started_at`, `run.finished_at` | UTC, ISO 8601, around the requests. |
| `run.duration_ms` | Wall-clock time between the two. |
| `run.concurrency` | Rows in flight at once. |
| `run.interrupted` | `true` when the run was cancelled; `dataset.rows_evaluated` is then short of the set. |
| `dataset.name`, `dataset.source` | The `--dataset` name and where the data comes from. |
| `dataset.revision`, `dataset.license`, `dataset.downloaded_at` | What the download script recorded (`null` when it recorded nothing). |
| `dataset.splits` | The splits included, in row order. |
| `dataset.rows_total`, `dataset.rows_evaluated` | Rows in the downloaded set, and rows in this file (the set, the sample, or fewer after an interruption). |
| `dataset.sample` | `null` for a full run, else `{ size, seed, method }`. |
| `dataset.positive_label` | What `label = 1` means, e.g. `injection`. |
| `dataset.stage` | The stage evaluated (`inbound`, `outbound`, `tool_call`, `grounding`). |
| `dataset.state_shape` | How each row's text reached the model, e.g. `{ user_message }`. |
| `policies.file`, `policies.sha256` | The policy file as given (forward slashes) and the SHA-256 of its bytes: which thresholds these rows were judged against. |
| `policies.stage` | The stage whose policies ran. |
| `policies.evaluated[]` | Those policies in file order: `id`, `type` (`noul`, `choice`, `score`), `thresholds` (`flag`, `review`, `block`; `null` for choice), `actions` (option to `{ action, min_confidence }`; `null` for noul and score), `min_confidence`, `on_error`. |
| `model.name` | `IDecisionModel.Name`, the alias sent: `typesafe:jev-latest`. |
| `model.resolved[]` | Every distinct release that answered (`jev-1.13.0`), sorted; usually one. |
| `summary.rows`, `summary.errors` | Rows in the file, and rows whose verdicts came from `on_error` because the model failed. |
| `summary.outcomes` | Rows per stage outcome, all four actions listed. |
| `summary.input_tokens`, `summary.output_tokens` | Sums over the rows that have them. |

The summary is for a quick look; the metrics (roadmap 4.2) are computed from the rows, never from the summary.

Row (`rows[]`):

| Field | Meaning |
|---|---|
| `id` | `split/index`, the source row. |
| `split`, `index` | The split and the row's index within it, as the source numbers it. |
| `label` | `1` for the positive class (`dataset.positive_label`), `0` otherwise. |
| `text_sha256`, `text_chars` | SHA-256 (hex) of the UTF-8 text and its length in UTF-16 characters; the text itself is not stored. |
| `outcome` | The stage outcome: the most severe action across the verdicts (`allow`, `flag`, `review`, `block`). |
| `latency_ms` | The engine's `StageResult.ModelLatency`, one decimal: wall-clock time of the model call, client retries included, the same measurement the `kassad.model.latency` histogram records (`Docs/specs/telemetry.md`). One value per row because one request carries every policy. |
| `input_tokens`, `output_tokens` | What the model reported for the request; `null` when the call failed. |
| `error` | Why the model call failed, from the first error verdict; `null` when it succeeded. |
| `verdicts` | One object per policy of the stage, keyed by policy id, in file order. |

Verdict (`rows[].verdicts.<policy_id>`); fields that do not apply to the question type are left out:

| Field | Applies to | Meaning |
|---|---|---|
| `type` | all | `noul`, `choice` or `score`. |
| `probability` | noul | The probability that the answer is yes. |
| `choice` | choice | The option the model selected. |
| `probabilities` | choice, score | Choice: option to probability, in the policy's option order (the API returns them in arbitrary order). Score: probability per level, in level order. |
| `score` | score | The probability-weighted level index. |
| `confidence` | choice, score | The model's confidence. Noul answers have none. |
| `value` | all, when answered | The value the policy thresholded (`Verdict.Value`): the noul probability, the score, or the probability of the selected option. |
| `action` | all | The action the policy resolved to, including `on_error` when the model failed. |
| `from_error` | all | `true` when the action came from `on_error`; the answer fields are then absent. |
| `error` | when `from_error` | The failure. |

One row from `results/2026-09-18-deepset.json`:

```json
{"id":"train/260","split":"train","index":260,"label":1,"text_sha256":"f862245312e333e42757d12aaae7c5aa16e13eddcdec0064c293a621e669a627","text_chars":118,"outcome":"block","latency_ms":152.8,"input_tokens":481,"output_tokens":62,"error":null,"verdicts":{"prompt_injection":{"type":"noul","probability":0.99,"value":0.99,"action":"block","from_error":false},"request_class":{"type":"choice","choice":"prohibited","probabilities":{"support":0,"general":0.01,"prohibited":0.99},"confidence":0.98,"value":0.99,"action":"block","from_error":false}}}
```

## Committed results

| File | Dataset | Rows | Errors | Policies | Model |
|---|---|---|---|---|---|
| `results/2026-09-18-deepset.json` | deepset, train + test | 662 | 0 | the sample file's two inbound policies | `jev-latest`, answered by `jev-1.13.0` |

## What we report

Per policy, per threshold: precision, recall, F1. Plus ROC AUC, a calibration table (predicted vs. observed), p50/p95
model latency, and cost per 1k checks at TypeSafe's quoted pricing. `report` lands in roadmap 4.2 and reads the files
above; roadmap 4.4 regenerates the README table from them by CI on a schedule and on tag. Hand-edited numbers in the
README are a bug.

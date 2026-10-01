# Awesome-list entries and announcement

Drafted 2026-10-01 against `0.1.0`. **Nothing in this file has been submitted or posted.** Every factual claim in an
entry traces to a sentence of the root `README.md` or a cell of its Numbers summary table, which is generated from the
runs committed under `eval/results/`. When an entry is submitted, record the pull request link in the table below; when
a list changes its format or rules, change the entry here first.

| Target | Section | Status | Pull request |
|---|---|---|---|
| `AbdelStark/awesome-typesafe-jev` | Community projects → Client libraries and integrations | drafted | none |
| `v-modal/awesome-jev-tools` | Verification & Guardrails | drafted | none |
| `cobanov/awesome-jev` | Agents, coding, and guardrails | drafted | none |
| TypeSafe Discord, Show and Tell | announcement | drafted | not posted |

## Facts this file rests on

- **What publishes a package.** `.github/workflows/release.yml` runs on the push of a `v*` tag and on nothing else:
  no `release` event, no `workflow_dispatch`. Its push step runs `dotnet nuget push 'artifacts/*.nupkg' ... --skip-duplicate`,
  so a version nuget.org already holds is reported and skipped rather than failing with 409. `v0.1.0` (an annotated tag
  on `d9e3c03`, the merge of PR #33) and its GitHub release have existed since 2026-09-23; this pass created neither,
  pushed no tag and published nothing. A second push of the tag would rebuild, retest and repack, skip the four pushes
  and, per `softprops/action-gh-release`'s documented behaviour, update the existing release rather than fail.
- **nuspec metadata is immutable per version on nuget.org.** The tags, descriptions and per-package READMEs added on
  2026-10-01 do not reach `0.1.0`. Its four packages keep what they shipped with: tags `llm guardrails ai-safety
  prompt-injection typesafe jev aspnetcore middleware` (plus `delegatinghandler httpclient` on `Kassad.AspNetCore` and
  `client sdk system-one` on `Kassad.TypeSafe`), the long descriptions, and the repository README as the package page.
  The new metadata ships with the next published version, whatever its number.
- **Repository metadata**, set 2026-10-01 with these commands (rerun them to restore it; the description is 340
  characters, from the README's first two sentences):

  ```bash
  gh repo edit jacob-berendsohn/kassad --description "Calibrated LLM guardrails for .NET. Runs every prompt, completion, tool call and citation past typed checks answered by TypeSafe's Jev, a System One decision model, and hands your code an Allow / Flag / Review / Block verdict with its probability and confidence. ASP.NET Core middleware and a DelegatingHandler for your provider HttpClient." --homepage "https://www.nuget.org/packages/Kassad.AspNetCore"
  gh repo edit jacob-berendsohn/kassad --add-topic dotnet --add-topic csharp --add-topic aspnetcore --add-topic nuget --add-topic llm --add-topic llm-guardrails --add-topic llm-security --add-topic ai-safety --add-topic prompt-injection --add-topic jailbreak-detection --add-topic grounding --add-topic guardrails --add-topic middleware --add-topic typesafe --add-topic jev --add-topic system-one
  ```

- **Release notes.** The `v0.1.0` release body is the `CHANGELOG.md` `0.1.0` section under the heading
  `## Changelog for 0.1.0 (2026-09-23)` (the changelog's own heading is `## [0.1.0] - 2026-09-23`), followed by
  GitHub's generated "What's Changed" list of pull requests #13 to #33 and the compare link. Making the body
  byte-identical to the changelog section would drop that list. It is two commands, not run, awaiting a yes:

  ```bash
  awk 'index($0, "## [0.1.0]") == 1 { f = 1 } index($0, "[Unreleased]:") == 1 { f = 0 } f' CHANGELOG.md > notes-0.1.0.md
  gh release edit v0.1.0 --repo jacob-berendsohn/kassad --notes-file notes-0.1.0.md
  ```

## Entries

One entry per list, in that list's own format and vocabulary. "Not affiliated with TypeSafe AI" is the wording the
first list uses for independent projects and is true of Kassad. Each entry is followed by where its claims come from.
In every pull request the author discloses being the project's maintainer; all three lists ask for that.

### 1. `AbdelStark/awesome-typesafe-jev`

The task names this list as `AbdelStark/awesome-typesafe`; GitHub redirects that name to `awesome-typesafe-jev`,
the repository's current name (checked 2026-10-01).

- **Section:** `## Community projects` → `### Client libraries and integrations`, where `TypeSafeAI.Net` is listed.
- **Format** (`CONTRIBUTING.md`, pull request template): one bullet, `- [Name](url) — one sentence: what it does,
  what is distinctive, and any limitation a reader needs to know`; alphabetical by display name, so `Kassad` goes
  after `json-render` and before `Laya for Node.js`; include the license, what data leaves the machine and one
  material limitation; no star counts, follower counts or speed claims; a README-only pull request is enough (CI
  checks the derived pages, a maintainer commits them); disclose affiliation in the pull request.
- **Entry** (save as `kassad-entry.md`, one line):

  ```markdown
  - [Kassad](https://github.com/jacob-berendsohn/kassad) — Apache-2.0 .NET guardrail engine with ASP.NET Core middleware and a `DelegatingHandler` that run every prompt, completion, tool call and citation past typed Noul, Choice and Score policies answered by Jev (or any `IDecisionModel`), one request per stage, and hand application code an `Allow` / `Flag` / `Review` / `Block` verdict with its probability and confidence; thresholds and the mandatory per-policy `fail_open` / `fail_closed` choice are JSON the loader validates at startup, and the README's numbers are generated from committed evaluation runs on public single-turn sets with the sample policy file, in-sample; the evaluated text goes to TypeSafe's API, streaming responses pass through unevaluated, and it is `0.1.0`, a first release whose public API can still change before `1.0`; not affiliated with TypeSafe AI.
  ```

  Sources: README License (Apache-2.0); Packages table (engine, middleware, `DelegatingHandler`, `IDecisionModel`);
  opening paragraphs (prompt, completion, tool call, citation; `Allow` / `Flag` / `Review` / `Block` with
  probability and confidence; one request per stage); "Rules the engine enforces" (Noul, Choice, Score; `on_error`
  mandatory; the loader rejects); "Why this exists" (thresholds are config, a JSON diff); Quick start comment
  (validates at startup); Status banner and Numbers bullets (generated from committed runs, public single-turn sets,
  sample policy file, in-sample, API may change before `1.0`); "What the policies see" (what reaches the model);
  Design notes (streaming passes through unevaluated).

- **Pull request body** (save as `kassad-pr-body.md`; the list's template):

  ```markdown
  ## What this changes

  Adds Kassad to Community projects → Client libraries and integrations, alphabetically between json-render and Laya for Node.js. README only.

  ## Why it belongs

  A .NET integration of Jev that is more than a client: a JSON policy format with a fail-fast loader, an engine that batches a stage's policies into one Jev request and resolves the answers into Allow / Flag / Review / Block in application code, ASP.NET Core middleware and a DelegatingHandler for the provider HttpClient, and an evaluation harness whose runs are committed. It shows the model shape (typed Noul, Choice and Score questions; thresholds in data, not prompts) end to end.

  ## Evidence and caveats

  The README's Numbers section is generated from `eval/results/` (hashes, labels and verdicts of every row, no dataset text) over four public datasets, answered by `jev-1.13.0`; the README states that the configured thresholds are in-sample and that the sets are not production traffic. Source that calls Jev: `src/Kassad.TypeSafe/TypeSafeClient.cs` and `src/Kassad.TypeSafe/SystemOneWire.cs`. Limitations in the entry: evaluated text goes to TypeSafe's API, streaming responses are not evaluated, 0.1.0 with an API that may change before 1.0.

  ## Affiliation

  I am the author and maintainer of Kassad. Not affiliated with TypeSafe AI.

  ## Checklist

  - [x] The resource is public and directly relevant to TypeSafe, Jev, or the System One interface pattern.
  - [x] The entry is in exactly one section and alphabetized by display name.
  - [x] The description is factual, concise, and free of star counts or promotional superlatives.
  - [x] Source code has a license and does not instruct users to commit credentials.
  - [x] I edited the canonical README entry rather than a generated project page or card.
  ```

- **Commands** (awaiting a yes; nothing below has been run):

  ```bash
  gh repo fork AbdelStark/awesome-typesafe-jev --clone --default-branch-only
  cd awesome-typesafe-jev
  git checkout -b add-kassad
  sed -i '/^- .json-render.(/r kassad-entry.md' README.md
  git diff
  git add README.md
  git commit -m "Add Kassad to Client libraries and integrations"
  git push -u origin add-kassad
  gh pr create --repo AbdelStark/awesome-typesafe-jev --head jacob-berendsohn:add-kassad --title "Add Kassad (.NET guardrail engine, ASP.NET Core middleware and DelegatingHandler)" --body-file kassad-pr-body.md
  ```

### 2. `v-modal/awesome-jev-tools`

- **Section:** `### Verification & Guardrails` in `README.md`. The section says its source file is
  `categories/verification-guardrails.md` and the README links a `CONTRIBUTING.md`, but on 2026-10-01 the repository
  held only `.github`, `.gitignore` and `README.md`, so the README is the file to edit.
- **Format** (`## Submission format`, `## Inclusion criteria`, `## Current coverage`): exactly one line per entry,
  `- [Name](URL) - Industry: one-sentence description of the Jev use case.`; each entry lives in exactly one category,
  the one closest to its direct application domain; excluded are generic classifiers that do not use Jev, pure theory,
  launch hype with no working artifact, long write-ups and private or vague sources; entries are listed in the order
  they were added, not alphabetically, so the new line goes after the section's last bullet (`jev-secret-detection` on
  2026-10-01; check before running the command). "Curation is not endorsement."
- **Entry** (save as `kassad-entry.md`, one line):

  ```markdown
  - [Kassad](https://github.com/jacob-berendsohn/kassad) - LLM application security: .NET guardrail engine, ASP.NET Core middleware and `DelegatingHandler` that batch every prompt, completion, tool-call and citation policy for a stage into one Jev request and resolve the typed answers in application code into an `Allow` / `Flag` / `Review` / `Block` verdict against thresholds in a validated JSON policy file with a mandatory per-policy fail-open or fail-closed rule; `0.1.0`, first release.
  ```

  Sources: as for entry 1 (Packages table, opening paragraphs, "Rules the engine enforces", Status banner).

- **Pull request body** (save as `kassad-pr-body.md`; the list has no template):

  ```markdown
  Adds Kassad to Verification & Guardrails, one category, at the end of the section in the list's order of addition.

  Working artifact: four packages on nuget.org at 0.1.0 (Kassad, Kassad.Abstractions, Kassad.TypeSafe, Kassad.AspNetCore), a runnable sample, and an evaluation harness whose runs are committed under eval/results/ and generate the README's Numbers section. Jev answers the typed policies of a stage in one request; application code owns the thresholds, the confidence floors, the per-policy fail-open or fail-closed rule and what happens to the verdict.

  I am the author and maintainer. Not affiliated with TypeSafe AI.
  ```

- **Commands** (awaiting a yes; nothing below has been run):

  ```bash
  gh repo fork v-modal/awesome-jev-tools --clone --default-branch-only
  cd awesome-jev-tools
  git checkout -b add-kassad
  sed -i '/^- .jev-secret-detection.(/r kassad-entry.md' README.md
  git diff
  git add README.md
  git commit -m "Add Kassad to Verification & Guardrails"
  git push -u origin add-kassad
  gh pr create --repo v-modal/awesome-jev-tools --head jacob-berendsohn:add-kassad --title "Add Kassad to Verification & Guardrails" --body-file kassad-pr-body.md
  ```

### 3. `cobanov/awesome-jev`

- **Section:** `## Agents, coding, and guardrails`. Its entries are alphabetical ignoring case, so `Kassad` goes
  after `Jevonian` and before `langchain-skill-router`.
- **Format** (`CONTRIBUTING.md`): `- [Project name](https://github.com/owner/repo) - What Jev decides and how the
  result is used.`; one factual sentence; say what Jev decides and what deterministic code does; no unsupported
  performance, safety or accuracy claims; disclose in the pull request if you built or maintain the project. The list
  is source-backed: its research notes pin, per entry, the README and the implementing file at a reviewed commit, and
  maintainers may ask for the file that calls Jev and an evaluation artifact, so the pull request links both.
- **Entry** (save as `kassad-entry.md`, one line):

  ```markdown
  - [Kassad](https://github.com/jacob-berendsohn/kassad) - .NET guardrail engine, ASP.NET Core middleware and `DelegatingHandler` where Jev answers typed Noul, Choice and Score policies about each prompt, completion, tool call or citation in one request per stage, and application code resolves the probabilities against JSON thresholds into `Allow` / `Flag` / `Review` / `Block` verdicts, sends low-confidence answers to review and applies each policy's mandatory fail-open or fail-closed rule when the model fails; `0.1.0`, first release, with README numbers generated from committed runs on public datasets (in-sample, sample policy file).
  ```

  Sources: as for entry 1, plus "Why this exists" (a flat distribution means "I don't know", usually `review`),
  "Threshold guidance" (a confidence floor sends unsure answers to review) and Design notes (model failures become
  verdicts via `on_error`).

- **Pull request body** (save as `kassad-pr-body.md`; the list has no template):

  ```markdown
  Adds Kassad to "Agents, coding, and guardrails", alphabetically after Jevonian.

  What Jev decides: the Noul, Choice and Score policies of a stage (inbound, outbound, tool_call, grounding), batched into one request per stage. What deterministic code decides: the thresholds and confidence floors that turn the probabilities into Allow / Flag / Review / Block, each policy's fail_open or fail_closed rule when the model fails, and what the application does with the verdict.

  Source that calls Jev, pinned at v0.1.0 (commit d9e3c03e19c782c65f38444cf8fe50d3cfb69fc5):
  - https://github.com/jacob-berendsohn/kassad/blob/d9e3c03e19c782c65f38444cf8fe50d3cfb69fc5/src/Kassad.TypeSafe/TypeSafeClient.cs
  - https://github.com/jacob-berendsohn/kassad/blob/d9e3c03e19c782c65f38444cf8fe50d3cfb69fc5/src/Kassad.TypeSafe/SystemOneWire.cs
  - https://github.com/jacob-berendsohn/kassad/blob/d9e3c03e19c782c65f38444cf8fe50d3cfb69fc5/src/Kassad/Engine/GuardrailEngine.cs

  Evaluation artifacts at the same commit: eval/results/2026-09-23-deepset.json, 2026-09-23-jailbreakbench.json, 2026-09-23-toxicchat.json and 2026-09-23-vitaminc.json (hashes, labels and verdicts per row; no dataset text), from which the README's Numbers section is generated. The README states that the figures for the configured thresholds are in-sample and were measured with the sample policy file on public single-turn sets, not on production traffic.

  I am the author and maintainer. Not affiliated with TypeSafe AI.
  ```

- **Commands** (awaiting a yes; nothing below has been run):

  ```bash
  gh repo fork cobanov/awesome-jev --clone --default-branch-only
  cd awesome-jev
  git checkout -b add-kassad
  sed -i '/^- .Jevonian.(/r kassad-entry.md' README.md
  git diff
  git add README.md
  git commit -m "Add Kassad to Agents, coding, and guardrails"
  git push -u origin add-kassad
  gh pr create --repo cobanov/awesome-jev --head jacob-berendsohn:add-kassad --title "Add Kassad (.NET guardrail engine and ASP.NET Core middleware)" --body-file kassad-pr-body.md
  ```

## Announcement for the TypeSafe Discord

For the official server that awesome-typesafe-jev lists under Community and updates (`https://discord.gg/typesafe`),
in its Show and Tell channel. Under 150 words. Not posted.

> Kassad is an Apache-2.0 .NET guardrail engine for LLM applications: ASP.NET Core middleware and a DelegatingHandler run every prompt, completion, tool call and citation past typed Noul, Choice and Score policies answered by Jev, one request per stage, and hand your code an Allow / Flag / Review / Block verdict with its probability and confidence.
>
> What is different: verdicts are typed, thresholds are JSON you review in a diff instead of prompt text, and the policy loader fails fast at startup on a malformed file, including a policy without an explicit fail_open / fail_closed.
>
> Numbers: the sample policy's prompt_injection check scores ROC AUC 0.987 on deepset/prompt-injections (662 rows). That is in-sample, with the sample policy file on a public single-turn set, so measure on your own traffic first. The README's whole table is generated from committed runs.
>
> https://github.com/jacob-berendsohn/kassad
>
> 0.1.0, first release; the API may change before 1.0.

Sources: README License, opening paragraphs, "Why this exists", "Rules the engine enforces", Quick start comment,
Status banner, and the Numbers summary table row `deepset` / `prompt_injection` (662 rows scored, ROC AUC 0.987).

## What the lists ask for that Kassad does not yet have

- **awesome-typesafe-jev** asks every listing to say what data leaves the user's machine. The entry says it; the
  root README does not say it in one sentence (it follows from "What the policies see" and the client's purpose).
  A sentence in the README would close that; the README was read-only in the pass that wrote this file.
- **awesome-jev-tools** exists to answer "where is Jev already making real decisions in production workflows". Its
  inclusion criteria do not require production use, and Kassad has a working artifact, but Kassad claims no
  production deployment and the entry does not pretend otherwise.
- **awesome-jev** may ask for the file that calls Jev and an evaluation artifact; both are linked in the pull request
  body above, so nothing is missing there.
- All three expect the submitter to disclose being the maintainer; each pull request body does.

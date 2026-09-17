# Security

Kassad is a decision layer in front of language models. Bugs in it can let harmful content through
or block legitimate traffic, so we treat correctness issues in verdict resolution, policy validation,
and request handling as security issues.

## Reporting

Use GitHub's private vulnerability reporting on this repository (Security → Report a vulnerability).
Do not open a public issue for anything that could be exploited before a fix ships.

You will get an acknowledgement within 3 business days and a fix or a documented mitigation for
confirmed issues within 30 days. Reporters are credited in the release notes unless they ask not to be.

## In scope

- Verdict resolution producing a less severe action than the policy specifies.
- Policy validation accepting a file that should be rejected (missing `on_error`, out-of-range thresholds, unknown options).
- The middleware or handler skipping evaluation it should have performed (body handling, content-type detection, size limits).
- Rejection responses leaking policy internals when `IncludePolicyIdsInResponse` is false.
- Dependency vulnerabilities in packages we ship.

## Out of scope

- The decision model's judgments themselves. A calibrated probability that turns out wrong on a
  specific input is a model limitation, not a Kassad vulnerability. Threshold tuning is the
  operator's responsibility; see the README "Numbers" section.
- Deployments that set `fail_open` on destructive paths. The library requires an explicit choice
  precisely so this is a deliberate operator decision.
- Vulnerabilities in TypeSafe's service. Report those to TypeSafe.

## Supply chain

- Releases are published from GitHub Actions via NuGet Trusted Publishing (OIDC). No long-lived
  NuGet API key exists for this project.
- Packages include SourceLink and a symbol package so shipped binaries can be traced to a commit.
- Dependencies are pinned centrally in `Directory.Packages.props` and updated by Dependabot.

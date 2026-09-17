# Rejection responses

Status: implemented. This is the URI target of the `type` field in the problem+json body.

## Inbound middleware (`UseKassadInbound`)

When `StageResult.Outcome >= KassadOptions.RejectAt` (default `Block`):

```
HTTP/1.1 403
Content-Type: application/problem+json
Kassad-Outcome: block
```
```json
{
  "type": "https://github.com/__GITHUB_OWNER__/kassad/blob/main/Docs/specs/rejection-response.md",
  "title": "Rejected by Kassad",
  "status": 403,
  "detail": "Request rejected by policy.",
  "traceId": "0HN...",
  "policies": ["prompt_injection"]
}
```

`policies` is present only when `IncludePolicyIdsInResponse = true`. Default is false: the client learns that it was rejected and gets a trace id to quote to support; the operator's logs have the full verdicts keyed by that trace id.

Oversized bodies (`Content-Length > MaxBodyBytes`) with `OversizedBodyBehavior = fail_closed` return the same shape with status 413 and no `policies`.

Status code is configurable via `RejectionStatusCode` (must be 4xx/5xx).

## Delegating handler (`AddKassadHandler`)

The handler cannot short-circuit an ASP.NET pipeline; it is inside an `HttpClient`. It synthesizes a response so provider SDKs see a failed call:

```
HTTP/1.1 403
Content-Type: application/json
```
```json
{ "error": { "type": "kassad_blocked", "message": "Inbound rejected by policy", "policies": null } }
```

`type` is `kassad_blocked` for policy rejections and `kassad_oversized` (status 413) for bodies over the limit. Allowed responses carry `Kassad-Outcome: <allow|flag|review>` as a response header when `OutcomeHeaderName` is set.

## Not rejected

`Flag` and `Review` outcomes (below `RejectAt`) proceed. The middleware stores the `StageResult` in `HttpContext.Items` (read with `GetKassadInboundResult()`), and both middleware and handler emit the `Kassad-Outcome` header, so the application can implement "ask the user to confirm" or "queue for a human" itself.

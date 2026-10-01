# Kassad.AspNetCore

Inbound middleware for your endpoints and a `DelegatingHandler` for the `HttpClient` that talks to your LLM provider.
This is the ASP.NET Core package of Kassad, calibrated guardrails for LLM applications in .NET: every prompt,
completion, tool call and citation is run past a set of narrow, typed checks answered by a System One decision model
(TypeSafe's Jev by default), and your code gets an `Allow` / `Flag` / `Review` / `Block` verdict with the probability
and confidence behind it. Your code decides what to do; Kassad never does.

> **Status: `0.1.0`, first release.** The public API can still change before `1.0`.

## Install

```bash
dotnet add package Kassad.AspNetCore
dotnet add package Kassad.TypeSafe
```

## Quick start

```csharp
builder.Services.AddTypeSafe();                       // reads TYPESAFE_API_KEY
builder.Services.AddKassad("kassad.policies.json");   // validates at startup, fails fast
builder.Services.AddKassadAspNetCore();

// Screen your own endpoints
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/chat"), b => b.UseKassadInbound());

// Screen the calls you make to the provider
builder.Services.AddHttpClient("openai", c => c.BaseAddress = new("https://api.openai.com/"))
                .AddKassadHandler();
```

Blocked requests get a `403 application/problem+json`. Everything else proceeds with the `StageResult` attached to
`HttpContext.Items`, so an endpoint can act on `Review` or `Flag`:

```csharp
var inbound = http.GetKassadInboundResult();
if (inbound?.Outcome == VerdictAction.Review) { /* ask the user to confirm, queue for a human, ... */ }
```

By default an OpenAI chat-completions or Anthropic messages body reaches the policies as `user_message`,
`system_prompt` and `completion`; a body of any other shape is evaluated whole. Streaming (`text/event-stream`)
responses pass through the handler unevaluated with a warning. Rejections do not name the policy that fired unless
you opt in.

Depends on `Kassad` (the engine) and `Kassad.Abstractions`. Pair it with `Kassad.TypeSafe` or any `IDecisionModel`.

## Policies and numbers

The policy file format (`kassad.policies.json`), the stages and what the policies see, and the **Numbers** section,
generated from committed evaluation runs together with what those numbers do and do not mean, are in the
[repository README](https://github.com/jacob-berendsohn/kassad#readme).

Apache-2.0.

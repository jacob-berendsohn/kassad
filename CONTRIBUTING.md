# Contributing

Thanks for looking. A few things that will save us both a round trip.

## Before you write code

1. Read `PROJECT_CONTEXT.md`. It lists the decisions already made and the open questions with their
   default assumptions. A PR that relitigates a dated decision without new information will be closed.
2. Check `Docs/roadmap.md`. If your change is roadmap work, name the sub-phase in the PR.
3. Open an issue for anything that changes the policy file format or a public type. Those are
   breaking changes for everyone downstream.

## Standards (enforced by the build)

- Warnings are errors. Nullable is on. `.editorconfig` code-style rules are enforced in build.
- Every public member has XML docs (`CS1591` is an error in `src/`).
- Public API changes go through `PublicAPI.Unshipped.txt` in the affected project.
- Root-cause fixes only. A PR that adds a null check where a null should be impossible will be asked
  to find out why the null is there.
- Tests: unit tests must not need network or an API key. Live tests are marked `[Trait("Category", "Live")]`
  and skip themselves when `TYPESAFE_API_KEY` is absent.

## Running locally

```bash
dotnet build
dotnet test
dotnet pack -c Release -o artifacts   # inspect a .nupkg before publishing
TYPESAFE_API_KEY=... dotnet run --project samples/Kassad.Sample.ChatApi
```

## Commit and release

- Conventional-ish commits are appreciated but not enforced. Clear messages are.
- `CHANGELOG.md` gets an entry under Unreleased with every user-visible change.
- Releases are tags: `git tag v0.1.0-preview.3 && git push --tags`. CI does the rest.

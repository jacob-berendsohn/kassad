## What

<!-- One paragraph. What changes and why. Link the roadmap sub-phase if this is roadmap work. -->

## Root cause (for fixes)

<!-- What was actually wrong. "Added a null check" is a symptom fix and will be sent back. -->

## Checklist

- [ ] Tests cover the change (unit; live tests if the wire format is touched)
- [ ] `PublicAPI.Unshipped.txt` updated for any public surface change
- [ ] No policy defaults introduced for `on_error` (there must never be one)
- [ ] `CHANGELOG.md` updated under Unreleased
- [ ] `PROJECT_CONTEXT.md` updated if a decision or caveat changed

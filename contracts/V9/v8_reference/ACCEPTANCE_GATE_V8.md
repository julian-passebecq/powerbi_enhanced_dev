# V8 Acceptance Gate

Stop after this semantic-IDE boost gate.

## Upstream
- relevant TE2 PRs reviewed
- adopted patches are pinned/documented
- tests added
- no blind open-PR cherry-picks

## UDF/TMDL
- UDF create/edit works
- folder serialization is Git-friendly
- TMDL round-trip fixtures pass
- relationship and descriptions fidelity tests exist

## DAX
- query tabs work
- execute/selection works
- multiple result sets work
- formatting handles UDF syntax
- Go/Peek definition minimum viable

## Data
- table preview works
- paging/fallback is explicit
- cancellation works
- DirectQuery limitations shown

## Automation
- selected MIT script patterns converted to typed actions
- source attribution kept
- preview/apply/validate/undo maintained

## Profiling
- at least column profile + relationship coverage implemented
- query cost/cancellation visible

## Background tasks
- long scripts/actions do not freeze UI
- cancellation/progress/output present

## CI
- PbiBench CLI can run BPA + semantic validation headlessly
- sample GitHub Actions workflow produces validation artifact

## Legal/source
- no proprietary TE3 implementation copied
- TE3 support repo not treated as source
- open-source components retain notices
- local/unverified rule packs remain local by default

Report screenshots, build/tests, adopted upstream PRs, and remaining Pass 3 candidates.

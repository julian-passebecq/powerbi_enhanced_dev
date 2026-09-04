# Master Prompt V8 — Boost TE2++ without turning PbiBench into a TE3 clone

## Baseline

Assume V7 has already delivered:
- reliable PbiBench launch,
- TE2 2.28 integrated in-process,
- coherent PbiBench shell,
- Automation/BPA UX,
- DAX Studio bridge,
- model diagram,
- PBIP/Git awareness.

V8 is a semantic IDE capability pass.

## Priority order

### P0 — upstream correctness first
Evaluate current TE2 PRs/issues and port only what is relevant and tested.

### P1 — Data Preview + DAX Query tabs
These deliver large day-to-day value without rebuilding DAX Studio.

### P2 — UDF/TMDL/Git maturity
Make modern DAX UDFs first-class and Git-friendly.

### P3 — Typed actions from official MIT scripts
Turn known scripts into safe, previewable product features.

### P4 — background execution
Long actions/scripts must not freeze the UI.

### P5 — code actions / multi-expression editing
Incrementally add modern IDE behavior.

### P6 — CI validation
PbiBench CLI should become useful in GitHub Actions/Azure DevOps.

## What NOT to prioritize

- full TE3 debugger parity
- pixel-identical TE3 UI
- TE3 DAX Optimizer implementation
- full PivotGrid clone
- proprietary source/asset reuse

For deep DAX performance:
route to DAX Studio.

For DAX optimization service:
create adapters only when licensing/API terms allow.

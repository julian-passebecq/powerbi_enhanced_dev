# Testing and safety

## Rule 1 — scan first
Every action must support read-only Scan.

## Rule 2 — idempotent where practical
Running a standardization action twice should not duplicate tables/measures/calc groups.

## Rule 3 — undo
For local model/file actions, keep before-state sufficient to undo the current session.

## Rule 4 — Git baseline
For PBIP/TMDL/PBIR changes, require a clean/acknowledged tree and offer baseline commit/snapshot.

## Rule 5 — parity tests with TE2
For reused/ported TE2 behavior, create golden tests comparing upstream TE2 result to PbiBench engine result.

## Rule 6 — test models
Create tiny fixtures for:
- star schema
- bad relationship cardinality
- implicit measures
- no date table
- calc group present
- Direct Lake metadata
- malformed TMDL
- PBIR enhanced report.

## DataForge regression
Use DataForge `truth_manifest.json` to assert:
- row counts
- dedup effects
- KPI totals
- relationship correctness
- SCD behavior
- expected report values.

## Trusted scripts
C# scripts are effectively arbitrary code.
Never auto-run imported scripts.
Display source + provenance + trust state.

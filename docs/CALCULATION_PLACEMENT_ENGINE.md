# Calculation Placement Engine

## Why this must exist

Power BI 2026 has several calculation surfaces. An AI tool that always generates measures is no longer good enough.

## Decision matrix

| Need | Default candidate |
|---|---|
| Transform row data during refresh | Power Query M custom column |
| Persist model row/table calculation | Calculated column/table |
| Reusable business KPI under filter context | Measure |
| Reusable parameterized DAX implementation | UDF |
| End-user transformation/filter applied to many measures | Calculation group |
| Calculation only meaningful inside one visual layout | Visual calculation |

## UDF vs calculation group

Use a **UDF** when:
- logic is implementation detail,
- parameters matter,
- different calls can pass different arguments,
- code reuse is the main goal.

Use a **calculation group** when:
- the report user should select the behavior,
- one transformation should apply uniformly across measures,
- a slicer/filter is the interaction model.

## Measure vs visual calculation

Consider a visual calculation when:
- the result is only needed on one visual,
- it depends on visual adjacency/hierarchy,
- the base aggregated values already exist in the visual,
- PREVIOUS/NEXT/FIRST/LAST/RUNNINGSUM/MOVINGAVERAGE etc. make the formula simpler.

Prefer a measure when:
- result is reused across reports/visuals,
- it must participate in model-level logic,
- a report-local virtual table would become too large,
- a visual calculation limitation blocks deployment/sharing needs.

## Benchmark rule

If both are plausible:
1. generate both implementations,
2. run representative visual query,
3. record:
   - elapsed time
   - storage engine queries
   - formula engine work
   - virtual table estimated/observed size
   - densification risk
4. choose using evidence.

Never optimize by intuition alone.

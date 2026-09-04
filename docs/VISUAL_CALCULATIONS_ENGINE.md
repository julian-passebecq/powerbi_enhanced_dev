# Visual Calculations Engine

## Core concept

Visual calculations are DAX calculations stored on a report visual rather than in the semantic model.

They operate on the visual matrix after base values have been produced.

## Important capabilities to model

Functions/patterns:
- PREVIOUS
- NEXT
- FIRST
- LAST
- RUNNINGSUM
- MOVINGAVERAGE
- RANGE
- LOOKUP
- LOOKUPWITHTOTALS
- COLLAPSE / COLLAPSEALL
- EXPAND / EXPANDALL
- ISATLEVEL
- axis parameters
- reset parameters such as HIGHESTPARENT.

Templates:
- running sum
- moving average
- versus previous/next/first/last
- percent of parent
- percent of grand total
- average of children.

## PbiBench UI

For every visual calculation:
- Name
- DAX
- visual fields available to it
- hidden helper fields
- axis
- reset
- data type
- format string
- dependencies
- benchmark
- fallback semantic measure.

## Safety

Visual calculations can only reference content available on the visual.

The editor must therefore:
- resolve all references against the visual field list,
- flag unsupported relationship-dependent functions,
- warn about publishing/export limitations,
- validate PBIR/Power BI Desktop rendering.

## Performance

SQLBI's 2026 benchmark demonstrates both directions:

Good case:
- heavy base measure
- small visual virtual table
- visual calculation reuses already-computed values
- avoids repeated model queries.

Bad case:
- large multidimensional visual virtual table
- visual calculation forces VISUAL SHAPE/densification
- full leaf-level matrix becomes expensive.

PbiBench should estimate:
`row combinations x column combinations x hierarchy leaf combinations`

and label:
- low densification risk
- test required
- high densification risk.

Then benchmark.

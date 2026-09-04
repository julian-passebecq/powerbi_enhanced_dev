# Data Exploration Specification

## Data Preview

Right click table -> Preview Data.

Requirements:
- multiple table tabs
- virtualized WPF grid
- cancel
- refresh
- copy/export
- sort/filter where query mode permits
- clear storage mode badge
- query duration

Import:
- use DAX paging with `WINDOW` when supported and stable ordering/key is available
- otherwise explicit first-N fallback.

DirectQuery:
- avoid pretending full client paging exists when it cannot be guaranteed
- show first-N / server-query behavior clearly.

Direct Lake:
- preview through the supported connected engine path
- warn that large preview operations may force columns into capacity memory.

## Profile Data

Column:
- rows
- distinct
- blanks
- min/max
- mean/median/stddev for numeric
- top values
- string quality
- outliers
- date gaps.

Relationship:
- FK distinct
- unmatched FK
- FK coverage
- PK distinct
- unused PK
- PK coverage.

All expensive operations:
- background task
- cancel
- visible query
- elapsed time.

## Pivot Lab

Original PbiBench implementation.

UX:
- Rows
- Columns
- Values
- Filters

Drag/drop model fields.

Prefer generating DAX `SUMMARIZECOLUMNS` queries rather than reproducing TE3's MDX implementation unless MDX is specifically required.

Features:
- auto refresh toggle
- totals
- filters
- save layout as PbiBench JSON
- generate/show DAX
- convert layout to regression test.

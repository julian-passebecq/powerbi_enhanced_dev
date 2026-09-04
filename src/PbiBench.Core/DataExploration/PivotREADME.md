# Pivot Lab engine

`PivotQueryBuilder.Build(layout, schema)` validates every field against the current model and
returns read-only DAX, ordinal result bindings, execution warnings, and a detached layout.
Rows and Columns contain model columns. Values contain model measures or explicit aggregations.
Numeric Sum/Average and numeric/date Min/Max are checked before generating a query; Count counts
nonblank values and DistinctCount follows DAX's distinct-count semantics, including blank.

The builder uses SUMMARIZECOLUMNS with an independent rollup group for each axis. Enabling totals
adds a grand total for that axis; it does not introduce intermediate hierarchy subtotals.
Total cells are evaluated by the engine, preserving ratios, distinct counts, and filter context.
The client reshapes returned cells and never sums them to fabricate totals.

Result projection order is Rows, Columns, Values, RowTotalFlag, ColumnTotalFlag. Both flags are
always returned and distinguish a genuine blank member from a total. Generated aliases are
checked against model object names. All grouping keys and flags participate in deterministic
ordering. TOPN requests the configured limit plus one so the shared query service can report
truncation. This limits returned rows; it does not bound underlying model evaluation cost.

Filters use typed literals and schema-resolved identifiers. Equals/In use TREATAS; other
operators use FILTER over the column's values. Every filter is wrapped in KEEPFILTERS, so
multiple conditions intersect consistently. Blank measure combinations follow the engine's
normal SUMMARIZECOLUMNS suppression behavior.

`PivotLayoutStore` writes versioned JSON using an atomic sibling-file replacement. It rejects
unknown versions, invalid shapes and oversized files. Call Build again after loading to check
the layout against the current model; removed or renamed fields produce actionable errors.

`PivotTestArtifact.Create` accepts only a completed, untruncated single result whose exact DAX
and column bindings match the plan. It stores column names/types, row count and a canonical
SHA256 over ordered typed cells. `Verify` detects schema, count, value and order changes.
Null and DBNull use the same blank representation; blank members and totals remain different
because their subtotal flags are included. Tests are exact snapshots without numeric tolerances.

Public behavior requirements:
- https://learn.microsoft.com/en-us/dax/summarizecolumns-function-dax
- https://learn.microsoft.com/en-us/dax/rollupaddissubtotal-function-dax
- https://learn.microsoft.com/en-us/dax/treatas-function-dax

Implementation is original PbiBench code using public DAX behavior, with no TE3 source or assets.

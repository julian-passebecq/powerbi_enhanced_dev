# V9.2 data profile query contract

`DataProfileBuilder` is original PbiBench code. It generates reviewable DAX and performs no I/O. The Data workspace runs a selected plan through the existing independent TOM query service, with cancellation, timeout, visible executed query, bounded result materialization, and CSV export.

Profiles aggregate the connected engine's visible data. They never calculate whole-model statistics from a first-N preview. RLS remains enforced by the engine. Every profile carries a full-scan warning; DirectQuery/Dual/Mixed and Direct Lake add source-work or capacity-memory notes. Top-value/outlier/relationship samples are bounded, while aggregation work can still scan the full column or table.

## Column definitions

- Counts distinguish physical rows, distinct values including a physical blank, blank rows, nonblank rows, and distinct nonblank values. `DISTINCT` avoids the unknown blank member that [`VALUES`](https://learn.microsoft.com/en-us/dax/values-function-dax) can introduce for missing relationship matches.
- Numeric summaries include min/max, mean, median, and population standard deviation. Nonblank input is explicit because [`MEDIANX`](https://learn.microsoft.com/en-us/dax/medianx-function-dax) includes blanks. [`STDEVX.P`](https://learn.microsoft.com/en-us/dax/stdevx-p-function-dax) is guarded for fewer than two values; a singleton population has deviation zero, and an empty population remains blank.
- Advanced numeric profiles use inclusive quartiles and fences `Q1 − 1.5 × IQR` and `Q3 + 1.5 × IQR`. Outlier counts/fractions use nonblank rows, with a bounded frequency sample. These are review candidates, not automatic errors. See [`PERCENTILEX.INC`](https://learn.microsoft.com/en-us/dax/percentilex-inc-function-dax).
- Text profiles include length statistics. Advanced checks detect empty text, ASCII whitespace-only values, exact changes under TRIM, nonbreaking spaces, numeric/date parsing candidates, and IQR length candidates. [`TRIM`](https://learn.microsoft.com/en-us/dax/trim-function-dax) does not remove nonbreaking spaces, so they are counted explicitly. [`DATEVALUE`](https://learn.microsoft.com/en-us/dax/datevalue-function-dax) uses model locale, can try alternate formats, and can infer a missing year from the current year; its output is a parsing candidate count, not a proposed conversion.
- Advanced date profiles reduce timestamps to calendar days, count missing days only inside the observed range, and return the largest intervals between consecutive observed days. They do not generate an unbounded calendar. Predecessor lookup over distinct days can still be expensive. Day values use [`INT`](https://learn.microsoft.com/en-us/dax/int-function-dax) and [`CONVERT`](https://learn.microsoft.com/en-us/dax/convert-function-dax), avoiding date reconstruction rules for small year numbers.
- Boolean profiles expose true/false/blank counts instead of unsupported boolean MINX calculations. See [`MINX`](https://learn.microsoft.com/en-us/dax/minx-function-dax).
- Frequencies are computed with [`SUMMARIZE`](https://learn.microsoft.com/en-us/dax/summarize-function-dax). Samples use an explicit secondary value ordering and outer ORDER BY; [`TOPN`](https://learn.microsoft.com/en-us/dax/topn-function-dax) alone does not guarantee output order and can exceed N on ties. The query service still caps retained rows.

## Relationship definitions

Many-to-one relationships are oriented from the many side (FK) to the one side (PK), even when the metadata endpoints are reversed. Other cardinalities use From/To labels without asserting key semantics. Inactive relationships remain inactive.

Each endpoint has a distinct nonblank key set. `EXCEPT(FK, PK)` counts unmatched FK values; `EXCEPT(PK, FK)` counts unused PK values. Coverage divides matched distinct keys by the endpoint's distinct nonblank keys; a zero denominator stays blank. Blank rows and duplicate PK rows are reported separately. [`EXCEPT`](https://learn.microsoft.com/en-us/dax/except-function-dax) compares corresponding columns without coercing types, so mismatched endpoint types receive an explicit warning.

## Verification limits

Automated generator regressions cover schema escaping, alias collisions, type-specific statistics, blank behavior, standard-deviation guards, advanced-profile opt-in, IQR bounds, date-gap generation, relationship direction/cardinality, sample bounds, culture invariance, storage warnings, and preservation of typed result values. These verify query generation and adapter boundaries. Successful execution of these generated profiles against a populated engine requires an available live catalog; empty local XMLA servers are not reported as successful profile execution.

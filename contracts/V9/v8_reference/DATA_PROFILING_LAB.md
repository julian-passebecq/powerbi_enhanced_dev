# Data Profiling Lab

The MIT Scripts repository contains a particularly valuable new direction: data profiling macros.

Turn them into a real PbiBench `Data Profile` surface.

## Initial profiles

### Column profile
- type
- row count
- distinct count
- blank count
- min/max
- numeric mean/median/stddev where applicable

### Distribution
- compact histogram/spark distribution
- top values
- percent distinct

### Relationship coverage
For every relationship:
- distinct FK values
- unmatched FK values
- FK coverage %
- distinct PK values
- unused PK values
- PK coverage %

This is especially useful for DataForge truth checks.

### String quality
- whitespace
- numeric-as-text
- date-as-text
- length anomalies

### Outliers
- IQR-based low/high values
- outlier count/percentage

### Date gaps
- consecutive date spacing
- suspicious gaps

## UX

Right-click table/column:
`Profile data`

Open as tabs.

Make expensive scans explicit.
Connected-mode queries must have:
- cancellation
- row/query limits
- elapsed time
- query visibility/logging.

Do not silently scan huge DirectQuery/Fabric sources.

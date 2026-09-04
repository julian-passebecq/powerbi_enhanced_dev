# Model Quality / Optimization Specification

## VertiPaq

Integrate an open/compatible VertiPaq analysis path.

Surface:
- table/column size
- cardinality
- dictionary/data/hierarchy size
- partitions
- relationship RI issues
- temperature/usage where available.

Actions:
- sort by memory
- navigate to column
- profile
- create optimization finding
- compare before/after benchmark.

## Optimization Cockpit

Combine:
- BPA
- VertiPaq
- DAX dependency complexity
- DAX query tests
- relationship profiling
- storage mode
- Direct Lake warnings.

Recommendation classes:
- correctness
- maintainability
- size
- refresh
- query performance
- report risk.

## External optimizer adapter

If the user has access to a licensed external DAX optimizer/service, PbiBench may launch/integrate it through supported public APIs.

Do not reproduce proprietary optimizer rules by copying output or implementation.

## Semantic tests

First-class:
- scalar assertions
- table assertions
- snapshot result
- expected row count
- tolerance
- A/B comparison
- DataForge truth manifest adapter later.

Store tests as versioned project artifacts.

# DAX Studio integration / DAX Lab

## License decision

DAX Studio uses the **Microsoft Reciprocal License (Ms-RL)**.

That license permits modification/distribution but requires source disclosure under Ms-RL for any distributed file containing DAX Studio code.

Therefore:

### Default PbiBench approach
- treat DAX Studio as an external tool/reference,
- use public ADOMD/TOM interfaces for original code,
- optionally call DAX Studio command-line tooling as a separate process,
- do not copy DAX Studio source files into PbiBench permissive core by default.

### If code is ever reused
- isolate it in clearly identified Ms-RL files/assembly,
- preserve notices,
- distribute source for those files,
- keep licensing boundary explicit.

## Feature references worth studying

DAX Studio current source/docs are useful for:
- connection management
- DAX editor behavior
- query execution
- metadata panes
- server timings
- query plans
- benchmarks
- export
- UDF code completion
- custom calendar support.

## PbiBench DAX Lab

Build original services:

```text
IDaxConnection
IDaxQueryExecutor
IDaxQueryHistory
IDaxBenchmarkRunner
IDaxServerTimings
IDaxQueryPlanProvider
IDaxTestRunner
IDaxCompletionProvider
```

UI:
- editor
- tabs
- model explorer
- result grid
- timings
- physical/logical query plan
- test panel
- diff/update proposal.

## DaxStudio.Controls

The uploaded DaxStudio.Controls snapshot contains useful reusable-control ideas, including a virtualized TreeGrid, but no explicit license file was found in the supplied archive.

Do not copy it until license provenance is confirmed.

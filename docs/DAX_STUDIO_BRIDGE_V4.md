# DAX Studio Bridge

## Decision

Keep DAX Studio as a separate application.

Do not embed its UI and do not merge its source into the PbiBench permissive core.

PbiBench should provide a first-class bridge.

## Bridge modes

### 1. Open current model in DAX Studio

PbiBench already knows:
- server / localhost port
- database/model name
- PBIX/PBIP name.

Launch DAX Studio with the current connection.

### 2. Open generated query

PbiBench writes a temporary/project `.dax` file and launches DAX Studio with:
- current server
- database
- file.

Use cases:
- generated regression test
- visual query from Performance Analyzer
- benchmark variant
- UDF test
- visual-calculation base query.

### 3. Headless DSCMD

Use `dscmd` externally for deterministic tasks where appropriate:
- execute DAX and export CSV/XLSX
- generate VPAX
- CI smoke tests.

Keep DSCMD optional.

## PbiBench native DAX subset

Implement natively:
- DAX text editor
- model metadata completion
- query execution
- result grid
- query history
- saved `.dax` project files
- test runner
- benchmark comparison summary.

Keep specialist analysis external initially:
- Server Timings timeline
- physical/logical query plan
- advanced traces.

This produces a useful DAX experience without rebuilding DAX Studio.

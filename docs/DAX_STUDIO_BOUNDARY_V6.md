# DAX Studio boundary V6

## Decision

DAX Studio remains **standalone but deeply integrated**.

Why:
- mature specialist query/performance tooling,
- current active maintenance,
- Microsoft Reciprocal License boundary,
- no value in recreating Server Timings/query-plan UI in Pass 1.

## Pass 1 integration

PbiBench Model/DAX editor provides:

`Open in DAX Studio`

Context passed:
- server
- database
- temporary/current `.dax` file.

Official DAX Studio startup arguments support:
- `--server`
- `--database`
- `--file`

## Pass 2+

Add:
- DAX query tabs/results in PbiBench for routine use,
- `dscmd` process adapter,
- benchmark/export results,
- optional VPAX generation,
- attach performance evidence to a PbiBench QA record.

## User experience

The user should not have to manually copy connection strings.

PbiBench decides:

```text
Routine expression/test -> do it here
Deep timings/query plan -> Open DAX Studio
```

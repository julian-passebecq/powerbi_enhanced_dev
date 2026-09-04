# DAX Studio Boundary V5

## Decision
DAX Studio stays installed separately.
PbiBench integrates it as a specialist application and command-line engine.

Reason:
- DAX Studio is mature and actively maintained,
- advanced Server Timings/query plans/VPAX are expensive to recreate,
- its Ms-RL license is different from TE2/PbiBench permissive code.

## PbiBench DAX Lab Light
Implement internally:
- editor
- model metadata completion
- run selected query
- result grid
- saved `.dax` project tests
- DAX regression tests
- UDF tests
- visual calculation tests
- DataForge expected results
- simple benchmark orchestration.

## DAX Studio UI bridge
Support configurable `daxstudio.exe` path and launch:
- `--server`
- `--database`
- `--file`.

## DSCMD bridge
Support external commands:
- query to CSV/JSON/XLSX
- BENCHMARK
- VPAX
- export if user explicitly requests.

Do not pass passwords on the process command line from PbiBench.
Prefer existing user auth/session or safe supported identity flow.

## User experience
PbiBench action:
`Advanced analysis in DAX Studio`

It generates/opens the selected `.dax` query and connects DAX Studio to the same model.

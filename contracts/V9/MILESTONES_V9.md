# V9 Milestones

## V9.1 — DAX IDE Core
Ship:
- DAX Language Service
- autocomplete
- function signatures
- object/variable completion
- diagnostics
- Go/Peek definition
- navigation history
- DAX query tabs
- run all / selection / current statement
- result grid
- query history
- PBIP `.dax` file support
- UDF syntax regression suite

Gate:
Routine measure/query development can be done without DAX Studio.

## V9.2 — Data Exploration
Ship:
- Data Preview
- multi-table tabs
- virtualized grid
- paging/fallback rules
- Profile Data
- relationship coverage
- Pivot Lab
- cancellation and query-cost visibility

Gate:
Developer can inspect/test model data without Excel/Power BI.

## V9.3 — Model Authoring Pro
Ship:
- UDF Workbench
- Calendar Editor
- Perspective Editor
- Translation Editor
- Table Groups
- Diagram+
- DAX Scripts
- model-wide find/replace
- safe code actions

Gate:
Most day-to-day TE3-class semantic authoring workflows exist in PbiBench.

## V9.4 — Automation / QA / Optimization
Ship:
- C# Script Preview
- trusted script mode
- action recorder
- macro library
- built-in PbiBench BPA packs
- VertiPaq analysis
- optimization cockpit
- DAX tests/assertions/snapshots
- background task queue

Gate:
Bulk work is safer and more testable than raw TE2 scripting.

## V9.5 — Fabric / Refresh / Workspace
Ship:
- Fabric browser
- table import
- Direct Lake workflows
- preview from Fabric
- schema update
- advanced refresh
- Dual-State PBIP/TMDL workspace
- Git semantic diff

Gate:
PbiBench is useful for local and Fabric semantic-model engineering.

## V9.6 — CLI / Agent / Compiler
Ship:
- `pbibench` CLI
- structured JSON
- CI commands
- Agent page
- safe tool schema
- optional MCP
- semantic compiler prototype
- DAX package manager prototype

Gate:
The same engine works for GUI, CI and AI agents.

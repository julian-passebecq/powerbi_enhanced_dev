# Master Prompt V9

## Mission

Turn PbiBench's already-working TE2++ integration into a modern semantic-model development environment.

The desired result is not "free TE3".
It is an original PbiBench semantic IDE that solves the same major developer needs and then extends into PBIP/PBIR/Fabric/AI workflows.

## Non-negotiables

- Preserve the reliable TE2 2.28 baseline.
- Characterize TE2 behavior before refactoring it.
- Avoid broad rewrites of working semantic-model code.
- Put new capabilities behind PbiBench services/contracts.
- All model mutations remain undoable or snapshot/preview protected.
- Long-running operations never freeze the UI.
- DAX Studio remains the deep-performance specialist.
- No proprietary TE3 code/asset reuse.

## Capability target

### A. DAX authoring
Build:
- model-aware IntelliSense
- syntax/semantic diagnostics
- signatures/tooltips
- Go to Definition
- Peek Definition
- navigation history
- regex/model-wide find-replace
- rename variable
- select next/all occurrence
- query tabs
- partial/selection execution
- result grids
- DAX Scripts for multi-object editing
- safe code actions
- UDF-aware completion and navigation

### B. Data exploration
Build:
- table preview
- multiple preview tabs
- virtualized/infinite-style paging
- data profile
- Pivot Lab
- query history
- Fabric/Direct Lake preview behavior

### C. Semantic model special editors
Build:
- UDF Workbench
- Calendar Editor
- Perspective Editor
- Metadata Translation Editor
- Table Groups
- Diagram editing
- advanced relationship inspector

### D. Automation
Build:
- script preview
- trusted legacy script mode
- action recorder
- reusable macros
- typed Automation Gallery
- AI Assistant backed by the same action system

### E. Model quality / optimization
Build:
- built-in PbiBench BPA rule packs
- user/community rule packs
- VertiPaq Analyzer integration
- data/relationship profiling
- optimization cockpit
- benchmark-required recommendations
- adapter point for licensed external optimizers, not a clone

### F. Refresh / connected workflows
Build:
- background task queue
- advanced refresh dialog
- table/partition scopes
- refresh type
- parallelism
- development override profiles
- TMSL preview/export
- progress/cancellation

### G. Fabric / Direct Lake
Build:
- Fabric connection browser
- workspace/lakehouse/warehouse listing
- table/schema preview
- table import wizard
- Direct Lake on OneLake / SQL mode decisions
- Import mode
- mixed-mode validation
- source schema update

### H. Workspace / code-first
Build:
- local PBIP/TMDL state
- live Desktop/XMLA state
- explicit diff
- push/pull
- conflict detection
- file watcher
- semantic Git diff
- UDF per-file serialization
- DAX query files
- layout persistence

### I. CLI / agentic
Build own `pbibench` CLI:
- inspect
- list
- get/set
- script
- bpa
- query
- test
- refresh
- validate
- diff
- deploy

Requirements:
- JSON output
- non-interactive mode
- predictable exit codes
- stderr for diagnostics
- no secrets in logs.

The future Agent page and MCP layer call the same safe command/action services.

### J. Semantic compiler / bridge
Later:
- define a PbiBench intermediate semantic representation
- import Databricks Metric View YAML
- translate tables, dimensions, measures, relationships into tabular intent
- emit diagnostics
- never assume perfect one-to-one semantics.

Do not block core semantic IDE work on this.

## Order

Implement Milestones V9.1 to V9.6 in `MILESTONES_V9.md`.
Do not attempt everything simultaneously.

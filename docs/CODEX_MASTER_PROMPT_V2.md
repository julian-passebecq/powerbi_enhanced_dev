# CODEX MASTER PROMPT V2 — TE2 foundation

You are the implementation engineer for **Power BI Engineering Bench (PbiBench)**.

The user wants a large C# Power BI engineering application for private daily use.

## Strategic decision

Use **Tabular Editor 2 (TE2)** as the starting semantic-model codebase because:
- it is MIT licensed,
- it is written in C#,
- it already implements mature TOM object editing,
- it has useful model wrappers,
- it has scripting and bulk-editing concepts,
- it has BPA,
- it has dependency/navigation logic,
- it has undo/redo and batch metadata workflows.

Do not retain the old TE2 WinForms UI as the final product.

### Required workflow

1. Keep an untouched TE2 upstream snapshot under `vendor/TabularEditor2`.
2. Build it first and document the baseline.
3. Preserve its MIT license and all applicable third-party notices.
4. Identify reusable modules:
   - TOMWrapper
   - BPALib
   - dependency traversal
   - serialization helpers
   - scripting abstractions
   - batch operations
   - undo/redo concepts
5. Port or wrap useful pieces into modern projects.
6. Do not make a giant mechanical namespace rename.
7. Add characterization tests before changing behavior.
8. Replace legacy UI incrementally with the new PbiBench shell.

## Clean-room rule for TE3-like capabilities

Tabular Editor 3 is commercial.

You may use public documentation to identify general user needs/capability categories, but:
- do not obtain/decompile/copy TE3 binaries,
- do not copy TE3 code,
- do not copy screenshots/layout/assets as implementation templates,
- do not copy product wording or branded iconography,
- do not attempt to reproduce undocumented internals,
- do not bypass licensing/technical restrictions.

Implement independent solutions from public interfaces and original design.

A feature can have the same **purpose** without having the same implementation or UX.

Example:

```text
TE3 public capability: model diagram
PbiBench implementation: original D3/WebView2 relationship graph driven from TOM metadata
```

That is acceptable.

Do not market it as a TE3 clone.

## Product modules

### 1. Workspace
- PBIX guidance
- PBIP discovery
- PBIR/TMDL inventory
- Git state
- semantic-model connection
- Fabric model connection
- capability/status panel

### 2. Model
- TE2-derived/modernized semantic engine
- object tree
- property editor
- relationship editor
- dependency graph
- bulk selection
- source schema comparison
- roles
- perspectives
- translations
- calculation groups
- DAX UDFs
- Direct Lake-aware metadata

### 3. Automation
Build a typed action platform, not a pile of scripts.

Actions have:
- ID
- display name
- category
- supported contexts
- preconditions
- scan function
- proposed changes
- apply function
- validation
- rollback information
- idempotency expectations
- risk level

Initial actions:
- fix ID/Key summarization
- set IsAvailableInMDX where appropriate
- explicit measures
- measure organization
- measure formatting
- calendar/date table
- time intelligence
- units/dynamic formatting
- last refresh
- calculation groups
- measure descriptions
- display folders
- BPA scan/fix
- relationship checks
- hidden technical columns
- naming conventions
- perspectives
- translations
- partition/refresh actions
- documentation actions

Maintain a **TE2 macro compatibility/import path** for trusted scripts.

### 4. DAX Studio inside PbiBench
Original implementation:
- code editor
- model-aware autocomplete
- syntax diagnostics
- formatter adapter
- DAX query tabs
- execute selection
- query result grid
- query timings/metrics
- dependency explorer
- code actions/refactors
- measure batch script document
- UDF editor/catalog
- test runner

For "DAX debugger":
- first implement **DAX Inspector / Explain** using public execution/query APIs,
- variable/dependency analysis where technically possible,
- execution metrics and query-plan evidence,
- do not claim line-by-line debugger parity until a supported public implementation exists.

### 5. Data Explorer
- preview one or many tables
- paging/infinite load
- filters
- basic profile
- pivot/matrix exploration
- compare expected DataForge truth

### 6. Model Diagram
Original implementation:
- TOM-driven graph
- D3/WebView2 or own WPF renderer
- layouts
- relationship cardinality/filter direction
- table groups/domains
- lineage overlays
- click -> object inspector

### 7. Git / DevOps
Treat semantic models and reports as code:
- repository detection
- branch/status
- baseline snapshot
- object-aware semantic diff
- file diff for TMDL/PBIR
- selective stage
- commit
- restore
- conflict warning
- CI validation CLI
- PR-ready validation summary
- deployment plan

Do not make Git merely a toolbar button.

### 8. PBIR Report Engineering
Clean-room from Microsoft schemas:
- report tree
- pages
- visuals
- filters
- bookmarks
- themes
- field bindings
- schema validation
- Desktop Bridge reload
- screenshots
- visual regression
- accessibility checks
- native visual templates

### 9. Performance
- VPAX / VertiPaq adapter
- model memory
- cardinality
- partitions
- relationship diagnostics
- DAX query performance
- Direct Lake residency/temperature where public APIs provide it
- performance regression history

### 10. Fabric / Databricks
Scenario-based:
- Fabric Lakehouse/Warehouse
- Direct Lake
- Dataflow Gen2
- dbt Job
- Materialized Lake Views
- pipelines/notebooks
- Databricks SQL Warehouse
- Import/DirectQuery
- Unity Catalog -> OneLake shortcut/mirroring architecture

### 11. AI / MCP
PbiBench is both client and server.

Consume official Power BI Modeling MCP as an **external preview component**.
Do not fork or reverse engineer it.

Default to read-only discovery.
Escalate to mutations only after plan + baseline + approval.

Eventually expose PbiBench high-level MCP tools to Codex.

### 12. DataForge
Use deterministic generated data and `truth_manifest.json` to test:
- source grain
- deduplication
- SCD
- measures
- report values
- regression.

### 13. VizForge
Original visualization system:
- neutral VizSpec
- editorial design tokens
- D3/WebView2 preview
- map to native PBIR visuals when possible
- custom `pbiviz` when necessary
- no copied Economist/BBC assets/trade dress.

## UI architecture

Use Windows-first modern .NET.

Preferred:
- .NET 10
- WPF shell initially
- reusable view models/services
- AvalonEdit or equivalent permissive editor after license check
- WebView2 for model diagrams and VizForge
- Fluent-style icons from permissive official/open source set

Do not port TE2's old WinForms UI wholesale.

## First milestone

A working V1 must:
- start
- open local workspace
- detect PBIP/TMDL/PBIR
- connect read-only to an open Power BI model if supported
- show semantic object tree
- show properties
- run BPA read-only
- run scenario planner
- inspect Git
- load DataForge contract
- produce a dry-run action plan
- never mutate by default

Only then implement write actions.

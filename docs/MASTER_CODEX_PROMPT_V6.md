# MASTER CODEX PROMPT V6 — PbiBench + TE2++ integrated first

You are implementing **PbiBench**, a private Windows-first C#/.NET Power BI and Microsoft Fabric engineering IDE.

## Product mental model

There is ONE application:

```text
PbiBench
  |
  +-- Model       TE2++ integrated semantic model editor
  +-- DAX         everyday DAX authoring/testing
  +-- Automate    typed bulk actions and trusted macro bridge
  +-- PBIP/Git    source/workspace engineering
  +-- Report      PBIR engineering later
  +-- QA          BPA/tests/diffs/validation
  +-- Fabric      REST/XMLA/control plane later
  +-- Deploy      CI/CD later
  +-- Knowledge   Senior Playbook/SQLBI metadata
  +-- Agent       MCP/AI later
```

**Tabular Editor 2 is not merely a reference.**
Use its MIT source as the semantic-model foundation.

**DAX Studio is not embedded.**
It remains the standalone specialist profiler, launched from PbiBench when needed.

## Why this architecture

Official TE2 already supplies:
- edit all major TOM semantic-model objects/properties,
- metadata-only editing,
- copy/paste/drag/drop/undo/redo,
- batch changes,
- C# scripting/macros,
- BPA,
- dependencies.

Public TE3 documentation shows the productivity categories users want beyond TE2:
- modern DAX editor/code assist,
- DAX querying,
- data previews,
- pivot/matrix testing,
- model diagrams,
- workspace synchronization,
- DAX scripts,
- macro/action recording,
- VertiPaq integration,
- advanced refresh,
- perspectives/translations UI,
- Direct Lake/Fabric workflows.

PbiBench should implement the useful categories independently using public Microsoft APIs and original UX.
Do not copy commercial TE3 source, assets, screenshots, internal behavior or trade dress.

## PASS 0 — establish the TE2 baseline

1. Inspect `vendor/TabularEditor2-bundled/`.
2. Record bundled version.
3. Verify licenses:
   - TE2 MIT,
   - FastColoredTextBox license,
   - FastWildcardMatching license,
   - TreeViewAdv license,
   - any other third-party notices.
4. If network is available, fetch/pin TE2 2.28.0.
5. Build upstream TE2 unchanged.
6. Run available tests.
7. Record baseline failures without "fixing" behavior yet.

Do not begin a mechanical full-framework port before the upstream build is understood.

## PASS 1 — visible PbiBench + TE2++ product

### 1. PbiBench shell
Create/complete the modern shell:

Navigation:
- Home
- Model
- DAX
- Automate
- PBIP / Git
- Report
- QA
- Fabric
- Deploy
- Knowledge
- Agent

Header:
- current project/model
- source/connection
- Git branch/status
- Power BI Desktop connection
- Fabric connection later
- validation status.

### 2. Model = integrated TE2++
The Model experience must retain useful TE2 behavior:
- semantic object tree,
- property editor,
- measures,
- calculated columns/tables,
- relationships,
- calculation groups/items,
- roles,
- perspectives,
- translations,
- display folders,
- partitions where available,
- dependencies,
- batch selection/editing,
- copy/paste,
- undo/redo,
- BPA,
- C# scripting.

Do not rewrite every control in Pass 1.
A hosted/adapted legacy control is acceptable if it is cleanly isolated.

### 3. Automation pane
Turn scripts into discoverable actions.

Initial action contract:
- metadata,
- supported selection/context,
- scan,
- findings,
- plan,
- preview,
- apply,
- validation,
- rollback/undo,
- risk level.

Pass 1 actions:
1. Format selected/all measure DAX.
2. Explicit SUM measures from selected numeric columns.
3. Create/select measure table.
4. Set SummarizeBy None for selected key/ID columns.
5. Move selected measures to display folder.
6. Add descriptions from a simple template.
7. Last Refresh measure/table scaffold.
8. BPA scan and safe-fix subset.

The UI must show exact objects that will change.

### 4. Better BPA UX
For a finding show:
- severity,
- object,
- rule,
- reason,
- proposed change,
- before/after,
- source/rationale,
- preview,
- apply.

Do not auto-fix uncertain performance recommendations.

### 5. DAX basic UX
Inside PbiBench:
- improved expression editor,
- syntax highlighting,
- formatter,
- dependency navigation,
- saved scratch query,
- "Open in DAX Studio".

Do not attempt deep Server Timings in Pass 1.

### 6. DAX Studio bridge
Locate DAX Studio.
Launch:
- same server,
- same database,
- current `.dax` query file.

Use official startup arguments:
- `--server`
- `--database`
- `--file`

Later add dscmd.

### 7. Basic model diagram
Build an original relationship diagram:
- table nodes,
- fact/dimension visual distinction,
- relationship cardinality,
- active/inactive,
- filter direction,
- click table -> select it in Model editor.

D3/WebView2 is acceptable.
Do not copy TE3 diagram UX.

### 8. PBIP/Git awareness
If the active model belongs to a PBIP workspace:
- show PBIP root,
- semantic model folder,
- TMDL presence,
- PBIR presence,
- Git branch,
- dirty/clean,
- changed semantic files.

No automatic commit required in Pass 1.

### PASS 1 gate
See `docs/TEST_AND_ACCEPTANCE_V6.md`.

## PASS 2 — make TE2++ a real modern semantic IDE

Add:
- DAX Query tabs + result grid,
- data preview,
- multi-tab editor,
- autocomplete/model metadata completion,
- code actions/refactors,
- DAX script document for multi-object edits,
- UDF library/editor,
- Calendar editor/wizard,
- better Perspectives editor,
- better Translations editor,
- semantic object diff,
- TMDL source mode,
- live vs disk comparison,
- more Automation actions,
- VertiPaq adapter,
- Senior Playbook checks.

TE2 2.27+ already introduced UDF and Calendar object awareness plus TMDL import; leverage this rather than reimplementing basic object recognition.

## PASS 3 — PBIR/report engineering

Use Microsoft PBIR public schemas and first-party agentic guidance:
- report/page/visual/bookmark tree,
- native visual templates,
- field bindings,
- visual calculations,
- theme manager,
- report validation,
- Desktop Bridge reload,
- screenshots,
- accessibility,
- visual regression,
- Git semantic report diff.

Do not implement binary PBIX editing.

## PASS 4 — Fabric/Power BI control plane

Use public interfaces:
- Fabric REST,
- Power BI REST,
- XMLA/TOM,
- Fabric semantic model definition TMDL pull/push,
- workspaces/items,
- refresh,
- deployment,
- permissions,
- Variable Library awareness,
- admin/estate inventory,
- capacity/FinOps later.

Read-only first, then gated writes.

## PASS 5 — Agent / DataForge / VizForge

- Power BI Modeling MCP as optional external first-party transport.
- Desktop Bridge.
- PbiBench MCP server with high-level safe tools.
- DataForge deterministic truth tests.
- VizForge original visual spec/editor/custom-visual pipeline.
- SQLBI Knowledge Radar metadata + user notes.
- Senior Playbook recommendation engine.

## Golden rule

PbiBench is not "TE3 free clone."

It is:
- TE2's open semantic foundation,
- our own guided automation/workbench,
- code-first PBIP/TMDL/PBIR,
- integrated testing and Git,
- Fabric control plane,
- specialist DAX Studio bridge,
- original visualization/report engineering,
- safe AI orchestration.

That broader combination is the differentiator.

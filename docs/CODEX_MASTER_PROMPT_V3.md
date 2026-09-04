# CODEX MASTER PROMPT V3

Read V2 first. This file adds the 2026 calculation, DAX, SQLBI, and visual-authoring requirements.

## 1. Calculation Placement Advisor

Create a first-class service:

```text
CalculationPlacementAdvisor
```

Input:
- business intent
- required reuse scope
- report-local vs model-global
- user interactivity
- need for parameters
- need for end-user slicer choice
- refresh-time vs query-time
- visual hierarchy dependency
- performance evidence
- compatibility requirements

Output:
- recommended calculation type
- alternatives
- rationale
- risks
- validation plan.

Supported choices:
- Power Query custom column
- calculated column
- calculated table
- measure
- DAX UDF
- calculation group
- visual calculation.

Examples:
- reusable business KPI -> measure
- reusable parameterized implementation detail -> UDF
- user-selected transformation applied to all measures -> calculation group
- running sum / previous / moving average local to one visual -> consider visual calculation
- row-level source shaping at refresh -> Power Query M.

## 2. Visual Calculation Engine

Visual calculations are report-layer DAX attached to a visual, not semantic-model objects.

Implement:
- discovery
- editor
- templates
- functions catalog
- dependency checks against fields on the visual
- axis/reset parameters
- hidden support fields
- formatting
- validation
- performance test.

Templates:
- running sum
- moving average
- versus previous
- versus next
- versus first
- versus last
- percent of parent
- percent of grand total
- average of children
- lookup with context/totals
- conditional-format helper
- custom totals later.

Performance rule:
- never assume visual calculations are faster.
- benchmark visual calculation vs semantic measure when either is viable.
- include virtual-table size/densification risk in recommendation.

## 3. DAX Lab

Use DAX Studio as a reference and external interoperability target.

Do not blindly merge DAX Studio source into the permissive PbiBench core because DAX Studio uses Microsoft Reciprocal License (Ms-RL).

PbiBench DAX Lab should independently implement:
- model-aware editor
- DAX query tabs
- run selection
- query results grid
- query history
- parameters
- DEFINE/MEASURE workflow
- update-model proposal
- performance analyzer query import
- server timings adapter
- query plan adapter
- benchmarks
- UDF explorer
- visual calculation test harness
- measure regression tests
- DataForge expected-result assertions.

Where practical, use public Microsoft ADOMD/TOM interfaces or execute DAX Studio / dscmd as a separate optional process.

## 4. DAX Test Generator

AI-generated DAX is never accepted because one displayed value looks correct.

For every measure:
- identify filter-context classes
- edge cases
- totals/subtotals
- empty periods
- multi-selection
- date boundaries
- RLS if relevant
- calculation group interaction
- UDF paths
- visual calculation interaction.

Tests become repeatable DAX queries with expected results or invariants.

DataForge `truth_manifest.json` is the strongest oracle when present.

## 5. UDF Refactoring Assistant

Scan DAX for duplicated logic.

Suggest:
- model-independent UDF if logic can be parameterized and reused across models,
- model-dependent UDF when a concise model-specific helper is more useful,
- measure or calculation group when the behavior should be visible/interactable by report users.

UDFs are first-class semantic-model objects and can be authored/tested through DAX Query View / TMDL-compatible workflows.

## 6. SQLBI Knowledge Radar

Build a feature called `Knowledge Radar`.

It stores metadata, not copied articles.

Entry:
- source
- title
- URL
- publication date
- topics
- status
- user notes
- local sample-path
- relevant PbiBench subsystem
- "implemented as" action/rule/test
- last reviewed.

Seed with current 2026 SQLBI topics:
- AI measure testing
- AI/UDF refactoring
- visual calculation performance
- visual calculations / lattice
- UDF vs calculation groups
- model-dependent vs model-independent UDF
- REMOVEFILTERS in UDFs
- dynamic hierarchy formatting
- Direct Lake vs Import
- filter context / ALLSELECTED / REMOVEFILTERS
- matrix totals.

UI:
- article card
- "Open source"
- "Add note"
- "Attach sample"
- "Create test idea"
- "Create rule/action"
- "Review against current model"

Do not reproduce the article text.

## 7. Power Query M Lab

Use the user-supplied Unlicense PowerQueryM library as a safe optional code reference.

Also prefer Microsoft's current Power Query language-services project for syntax/intellisense.

Build:
- M editor
- snippets
- function library
- connector detection
- query lineage
- source privacy classification
- native SQL detection
- query folding hints later
- diff/test of query definitions.

## 8. VizForge Custom Visual Studio

The app should offer two report-visual paths.

### Native visual path
Prefer a native PBIR visual when it expresses the analytical intent.

### Custom visual path
When native is insufficient:

```text
VizSpec
 -> primitive composition
 -> data roles
 -> formatting model
 -> D3/TypeScript renderer
 -> Power BI visual API
 -> pbiviz project
 -> package
 -> local install/test
```

PBI VizEdit is product inspiration only.
Do not copy their code, gallery artwork, UI or pricing/licensing mechanics.

Use:
- Microsoft PowerBI visual tools
- Microsoft visual samples such as ForceGraph/MultiKPI as MIT references
- original VizForge UI/design system.

## 9. PBIR Visual Manager

The uploaded MIT `isHiddenInViewMode` / PBIR Visual Manager is a valuable permitted reference for:
- filter visibility
- layer order
- visual interactions
- batch processing
- presets
- audit export
- undo/redo.

PbiBench should integrate equivalent capabilities in its own unified report tree.

## 10. Git real-world requirements

Design around actual Power BI pain points:
- PBIP data cache should not be treated as source control content
- local code-first Git remains separate from large local data caches
- thin reports can avoid every developer needing a full local imported model
- short workspace paths should be recommended
- CI validation should gate deployment
- direct workspace Git integration is optional, not mandatory
- support environment-specific connection/deployment configuration.

## 11. Report authoring naming collision

The uploaded `microsoft/powerbi-report-authoring` library is the embedded-report authoring SDK.

It is **not** the same thing as the 2026 Power BI Agentic `power-bi-report-authoring` skill that edits PBIR files.

Keep these separate in code and docs.

## V3 acceptance

The first complete demo should prove:

```text
DataForge dataset
 -> model inventory
 -> BPA
 -> DAX measure
 -> AI-generated DAX tests
 -> optional UDF refactor
 -> report visual
 -> visual calculation alternative
 -> benchmark both
 -> PBIR validation
 -> screenshot
 -> Git semantic diff
```

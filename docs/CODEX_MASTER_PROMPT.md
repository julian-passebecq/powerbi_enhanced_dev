# Codex Master Prompt

You are implementing **Power BI Engineering Bench** (`PbiBench`), a private Windows-first C#/.NET power-user application for modern Power BI/Fabric development.

You have reference repositories in `references/`. Read their licenses before copying any code.

## Product objective

Create one engineering workbench that can:

- connect to a Power BI Desktop / XMLA / local PBIP semantic model,
- browse and modify tabular objects,
- run best-practice analysis and fixes,
- create/format/test DAX,
- generate common model structures,
- manage PBIP/TMDL/PBIR and Git,
- inspect/report visual definitions,
- render visual design previews,
- validate and test Power BI projects,
- orchestrate optional Microsoft/Fabric MCP tools,
- import deterministic DataForge datasets and truth manifests.

## Critical architectural decision

Do **not** rewrite TE2 capabilities from zero.

Use TE2 as a permissively licensed implementation reference and selective source foundation.

However, do **not** make the TE2 WinForms UI the final shell.

The target architecture is:

```text
PowerBIBench.App                 WPF .NET 10
PowerBIBench.Core                domain / plans / actions / validation
PowerBIBench.TabularEngine       modernized TE2/TOM-derived engine
PowerBIBench.BPA                 TE2 BPA-derived rules engine
PowerBIBench.Scripting           trusted script import/host later
PowerBIBench.PowerBI             PBIP/TMDL/PBIR/Desktop Bridge
PowerBIBench.Git                 baseline/diff/restore
PowerBIBench.Agent               MCP client/server + approvals
PowerBIBench.DataForge           deterministic test data contract
PowerBIBench.VizForgeBridge      WebView2 / D3 / custom visual build
```

Keep `vendor/TabularEditor2` intact with upstream notices if vendored into the implementation repo.

## TE2 reuse phases

### Phase A — baseline
- Build upstream TE2 untouched using its documented toolchain.
- Run existing tests.
- Record current behavior.

### Phase B — extract reusable core
Prioritize:
- `BPALib`
- `TOMWrapper`
- model object wrappers
- dependency/metadata traversal
- serialization/save concepts
- scripting contracts
- connection/discovery behavior

Avoid importing UI controls unless separately justified.

### Phase C — modernize
- Retarget reusable engine code toward `net10.0-windows`.
- Replace old `packages.config` dependencies with PackageReference where practical.
- Use current `Microsoft.AnalysisServices` packages.
- Isolate Windows Forms-only dependencies behind compatibility interfaces.
- Create tests comparing upstream TE2 and new engine outputs.

### Phase D — WPF shell
Use WPF UI for navigation/control primitives, then override styling with PbiBench's own editorial tokens.

## Product interaction model

Every automatic change uses:

```text
Scan -> Findings -> Proposed Changes -> Preview Diff -> Apply -> Validate -> Undo option
```

Never create a giant destructive "Fix Everything" button without showing findings.

Allow a saved `Standardize Model` profile that selects many actions, but it still runs Scan first.

## First action catalog

Implement these as typed operations, not ad-hoc scripts:

1. Calendar/date table validation and generator
2. Empty/central measure table
3. Last refresh metadata/table/measure
4. Explicit measures from aggregatable columns
5. Format all existing measures
6. Time-intelligence calculation group
7. Units calculation group
8. Best Practice Analyzer scan + safe autofixes

Then add:
- hide foreign keys / summarize-by rules
- descriptions/documentation
- PY / delta / delta % measure templates
- dynamic format strings
- perspectives/translations
- incremental-refresh helper
- semantic-model optimization / VertiPaq analysis

## Script compatibility

Support three levels:

### Native Action
Strongly typed, tested, idempotent; preferred.

### Imported Macro Definition
Read `MacroActions.json` and show metadata/tree. Do not execute automatically.

### Trusted C# Script
Later feature. Full-trust warning. User must explicitly allow a script source. No remote auto-execution.

The official `TabularEditor/Scripts` repository is MIT and is a safe source candidate after preserving notices.

The supplied Alexander Korn macro collection is useful feature research, but its repository does not expose an obvious root license in the supplied material. Do not paste that code into PbiBench until provenance/license is verified per script. Reimplement the behaviors as original typed Actions.

## Power BI project support

Use current first-party project surfaces:

- PBIP for code-first project workflows
- TMDL for semantic model definitions
- enhanced PBIR for report definitions
- TOM for semantic model operations
- Power BI Modeling MCP where useful
- Power BI Desktop External Tool registration
- Desktop reload/render/screenshot validation later

Do not implement binary PBIX reverse engineering or a custom PBIX<->PBIP converter.

## UI goal

The UI must feel like a professional BI engineering application, not a generic admin panel.

Visual direction:
- light theme first
- compact editorial hierarchy
- dark navy structural bars
- black/charcoal typography
- one original red accent
- Power BI yellow only where it conveys Power BI identity/status
- thin separators
- very limited shadows
- square/4px radius, not bubble cards
- Fluent line icons
- Segoe UI Variable + Cascadia Code
- excellent density for tables/tree/editor

Think "editorial data desk + engineering IDE", not "copy The Economist".

## Core layout

```text
Top command bar: Workspace | Connect | Scan | Apply | Undo | Git | Run QA

Left 22%:    Workspace / Model / Report object tree
Center 53%:  Tabs: Overview, DAX, TMDL, PBIR, Viz, Diff
Right 25%:   Properties / Action parameters / Findings
Bottom:      Output / DAX query results / validation / Git log
```

Add a Home page with a large **Model Standardization** panel showing the 8 initial actions using Fluent icons and Scan/Apply state.

## VizForge

Do not embed arbitrary D3 into WPF business logic.

Use:

```text
VizSpec -> WebView2 D3 preview
        -> native PBIR visual mapping when possible
        -> generated Power BI custom visual project when needed
```

Use Microsoft's current `PowerBI-visuals-tools` and D3 7.x. Keep all design themes original.

## Fabric cloud counterpart

Treat PBI Fixer / Semantic Link Labs as inspiration and optional Fabric adapter reference:
- scan-only / fix workflow
- report explorer
- semantic model explorer
- model/report BPA
- VertiPaq / Direct Lake analysis
- perspectives/translations
- screenshot/prototype concepts

PbiBench itself remains the strong local C# application.

## Acceptance rule

V1 is done when:
- TE2 reference builds independently,
- PbiBench opens a model/project read-only,
- object tree works,
- first 8 typed actions can Scan,
- at least 4 can Apply + Undo,
- changes are validated,
- Git diff is understandable,
- no arbitrary C# script execution is required.

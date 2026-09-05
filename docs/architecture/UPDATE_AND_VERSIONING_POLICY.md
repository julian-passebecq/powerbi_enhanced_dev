# Update and versioning policy

## Purpose

Avoid losing track of what changed when TE2, Microsoft Fabric packages/APIs, DAX syntax, or companion tools evolve.

## Update lanes

### Lane 1 - TE2 foundation

Owned files:
- `vendor/TabularEditor2-2.28.0/`
- `vendor/patches/te2-*`
- `PbiBench.ModelEditor`
- native TE2 boundary tests

Procedure:
1. fetch candidate TE2 version/commit;
2. inspect license/release changes;
3. rebuild clean upstream;
4. rebase/apply local patches explicitly;
5. run native model/editor/undo/script regressions;
6. package in isolated branch;
7. update provenance pin only after green gate.

Do not update TE2 as a side effect of a Fabric feature.

### Lane 2 - Fabric

Owned files:
- `PbiBench.Fabric`
- `PbiBench.FabricToolbox`
- Fabric transport/auth tests

MSAL, SqlClient and Fabric API changes are handled here. Semantic IDE consumes stable contracts/adapters.

Do not update Fabric dependencies as part of a TE2 upgrade unless required and documented.

### Lane 3 - DAX language/editor

Owned files:
- `PbiBench.Dax.LanguageService`
- DAX editor/query integration

Refresh public syntax/function metadata independently. Keep engine validation authoritative.

### Lane 4 - External tools

DAX Studio/Power BI Desktop/VS Code are launch/handoff integrations. Detect supported versions where useful; do not vendor or fork them without a separate explicit decision.

Gen-2 gives DAX Studio, Bravo, Power BI Desktop and VS Code distinct bridge lanes. Supported process arguments and applicability tests protect each lane; tool authentication stays in the companion.

### Gen-2 report and semantic lanes

`semantic-shell` owns the existing DiagramView/Semantic View and DAX workspace presentation; TE2 engine/source updates remain in `te2`. `pbip-pbir` owns the pinned Microsoft schemas, index and file transactions. `report-studio` owns the separate modern WPF process. `lineage` owns structural reference traversal and local declaration evidence. These three report lanes must not acquire TE2, TOMWrapper, App or ModelEditor dependencies. `csharp-language` owns the curated gallery contracts and existing Safe/Trusted host boundaries. `workspace-git`, Fabric services and Fabric Toolbox remain separately owned. See V2_SOURCE_INDEX.md for exact new source pins.

### Lane 5 - PbiBench-owned features

Automation, Data Exploration, QA, workspace logic, AI export, CLI, etc. evolve under PbiBench versioning.

## Component version manifest

`module_catalog.json` is the current runtime-readable ownership/version manifest. Feature Map joins module IDs to this catalog and shows lifecycle, capability version, runtime/process and update lane. `provenance.json` remains the source, license and upstream pin ledger. `FEATURE_CATALOG.md` joins all three source catalogs deterministically.

Module versions describe independently evolving capability revisions. Shared product assemblies still use product build versions; a module can span a portable library and its net48-only integration UI. Catalog framework lists record the supported runtimes across those components. Separate-process Toolbox keeps its own assembly/product version (0.3.0).

Active, Selective, Independent, Incubating, On demand and Later are development lifecycles, not prohibitions. Isolate useful work in its owning module/update lane so it can continue without forcing unrelated upgrades. New UI/process modules must declare their owners, contracts, tests and dependency boundaries before integration.

## Release naming

Avoid labels such as `v3` or `newv` for future major pushes.

Use product/area labels:
- `v11.0 - Compartmentalized Platform`
- `fabric-toolbox-v0.1`
- `semantic-ide-v11.1`

A top-level release may record exact component versions.

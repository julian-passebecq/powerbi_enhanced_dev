# Independent audit — V2 Pass 2

Audited SHA:
`70493e1a63064a7e6d2ec98c285d187a556834a3`

## Verdict

V2 Pass 2 is GREEN.

### Confirmed functionality
- Hosted workflows were renamed to product-neutral `PbiBench fast gate` and `PbiBench Windows Release gate`.
- Hosted CI now actually runs V2 tests, Report Studio build and process-isolation checks.
- Report Studio gained search, page navigation, zoom/fit, synchronized tree/wireframe/lineage and cached immutable report view state.
- Semantic/report impact review is integrated before semantic refactors; it explicitly says TOM and PBIR are separate transactions.
- TMDL semantic catalog now uses relative indentation and a versioned metadata-only `SemanticCatalogSnapshot`.
- C# Gallery provenance was split into implementation origin / reference / verification / execution mode.
- Fabric Toolbox has an explicit read-only `getDefinition` report snapshot flow with strict path/payload limits and no remote update.
- PBIR mutation compatibility is now tied to an explicit pinned write-policy contract instead of `version == 4.0`.

### Local verification reported by Codex
Final local gate records:
- complete solution build: 0 warnings / 0 errors
- 537 framework-specific tests, 0 failures / 0 skipped
- Semantic IDE smoke: 52 checks
- Report Studio smoke: 14 checks
- Fabric Toolbox smoke + output isolation

### Independent hosted CI
For this exact SHA, both push workflows completed successfully.

Fast gate passed:
- V11 net10 regression
- Fabric Toolbox build/tests
- V2 net10 tests
- Report Studio build
- process isolation

Windows Release gate passed:
- portable tests
- net48 module tests
- Fabric Toolbox Release build/tests
- V2 Release tests
- Report Studio Release build
- process isolation
- offline Report Studio WPF smoke

## Audit finding for Pass 3 — real compartmentalization smell

`PbiBench.ReportStudio.csproj` currently references:
- `PbiBench.Pbir`
- `PbiBench.DaxStudio`

Report Studio uses the DAX Studio project only for generic companion-tool/process launching.

That means a generic Desktop/VS Code launcher is physically owned by a DAX-Studio-specific assembly.

The module catalog currently declares Report Studio dependencies as only `pbir` + `lineage`, so the catalog and real project graph disagree.

Fix:
- extract generic `ProcessAdapter`, Windows command-line quoting, tool discovery/status/context and generic companion launch into `PbiBench.ExternalTools`;
- make `PbiBench.DaxStudio` depend on `PbiBench.ExternalTools`;
- make `PbiBench.ReportStudio` depend on `PbiBench.ExternalTools`;
- keep DAX-Studio-specific argument/query handoff in `PbiBench.DaxStudio`;
- update module/provenance catalogs and dependency tests.

This is a targeted architectural correction, not a large rewrite.

## Remaining validation boundary
No authenticated live Fabric report retrieval, live Desktop rendering, DAX Studio query, Bravo or XMLA behavior has been independently exercised. Keep these claims explicit.

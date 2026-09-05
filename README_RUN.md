# Run PbiBench — integrated TE2++ first pass

PbiBench is the main application. Its Model workspace hosts the pinned TE2 2.28.0 editor **inside the same process**. The compatibility host uses WPF + WinForms on .NET Framework 4.8 so TE2's model editing and C# scripting remain available. Gen-2 adds the existing diagram's Semantic View modes, DAX Workbench, a curated C# gallery and a separate .NET 10 Report Studio for local PBIP/PBIR engineering. See [Gen-2 usage and boundaries](docs/V2_PASS1_IMPLEMENTATION.md). Fabric authentication and remote operations stay in Fabric Toolbox.

## Build and test

From this repository in PowerShell:

```powershell
./scripts/build-pass1.ps1 -Configuration Release -InstallSdk
```

Requires Windows, Git and Visual Studio 2022 with .NET desktop build tools. The script can install the .NET 10 SDK under the current user's LocalAppData. It provisions pinned TE2, restores packages, builds the solution, runs product tests and the offline upstream regression subset, then runs a GUI smoke test. Build logs and test evidence are under `artifacts/build`. Online provisioning validates commit `75f10e331b8de0dda5c213180b9b8867b4a38191`; `-Offline` uses the supplied snapshot when necessary (NuGet dependencies must already be cached).

Start the resulting application:

```powershell
./src/PbiBench.App/bin/Release/net48/PbiBench.exe
```

Or create a portable folder with `./scripts/package-pass1.ps1 -Configuration Release`. Run `artifacts/PbiBench/PbiBench.exe` from that folder; keep its dependencies beside it.

## First workflow

1. Click **Demo model** to open a private working copy, or **Open model** for BIM/PBIP/TMDL/PBIT. **Connect** uses TE2's Desktop / XMLA connection workflow.
2. Use **Model** for the real TE2 tree, property grid, expressions, relationships, roles, perspectives, translations, dependencies and C# scripting. Existing menus and shortcuts remain available.
3. Select measures or columns in the model tree, then open **Automate**. Choose an action and inspect exact objects, properties and before/after values. **Apply local changes** validates the preview and creates a grouped TE2 undo operation. A stale preview is rejected.
4. **QA** provides explained findings with conservative fix previews. **Full TE2 BPA** opens the preserved upstream analyzer and rule manager.
5. **DAX** provides highlighted scratch queries, offline formatting and `.dax` saving. Use **Expression → DAX Studio** for the active model expression, or open the scratch query from DAX. Configure the executable if discovery cannot find it. Live sessions pass their server and database; disk-only models open a disconnected query.
6. **Model diagram** displays inferred table roles, cardinality, active/inactive styles and filter arrows. Click a table to select it in Model.
7. **PBIP / Git** detects the project, semantic folders, TMDL/PBIR, branch, dirty files and pending Desktop changes. It performs no automatic commits.

`Ctrl+K` opens workspace commands. `Ctrl+O` opens a model. `Ctrl+S` saves the model or the current DAX scratch query. Closing/replacing a dirty model requires the existing TE2 discard decision.

## V2 Pass 2 workflow

- **Apps / Tools → Report Studio** opens PBIP/PBIR in a separate modern process. Search by page/visual/semantic field, use the page selector and zoom, then inspect lineage and schema badges. Multi-report projects offer a chooser.
- In Report Studio, select visuals with Ctrl/Shift in **Visual selection** for batch visibility/title changes. Configure an action, inspect the exact files/diff, mark it reviewed and apply. Every write has a durable backup and a separate reviewed restore. Bookmark edits use tested schema versions; table/matrix formatting is detector-only.
- **Semantic View → Report Usage** opens selected-field usages in Report Studio. Native semantic rename/delete/refactor checks fresh report impact first. **Ctrl+P → Report impact** exports a semantic catalog or imports display-name annotation mappings. TOM and PBIR changes remain separate transactions.
- **Automate → Power BI C# Gallery** has 20 entries, search/categories/favorites/recent, provenance and selection compatibility. Advanced calculation-group/selector templates only insert draft text; trust and execution remain separate.
- **Fabric Toolbox → Workspaces → Get report definition** explicitly retrieves a selected Report into a new local snapshot and opens Report Studio. Credentials stay in Toolbox. Legacy reports remain read-only; no remote report update is added.

See `docs/V2_PASS2_IMPLEMENTATION.md` for supported versions, bounds and source evidence, and `docs/V2_PASS2_VERIFICATION.md` for validation. Use `scripts/invoke-v2-gate.ps1` for the impacted Release gate.

## Original foundation and retained boundaries

- Seven actions are implemented: format measures, explicit SUM measures, create/use a measure table, SummarizeBy None, display folders, description templates and a Last Refresh scaffold. New calculated/refresh tables contain metadata and require an explicit subsequent data refresh.
- All new automation is local until the user saves/deploys. Native remote save/deploy paths review their model JSON or exact TMSL through an `ApprovedChangePlan` before writing. Arbitrary C# scripts remain trusted, unsandboxed TE2 code; these callbacks are not a security sandbox for user-authored code.
- DAX Studio remains a separate process for deep timings and query plans. Report Studio and Fabric Toolbox remain separate processes; the Pass-2 report flow is local engineering and read-only Fabric retrieval.
- The relationship layout is deliberately basic; table roles are inferred from relationship cardinality. DAX formatting is conservative and token-preserving, with no online formatter call.
- See `docs/BASELINE_V6.md`, `docs/TE2_INTEGRATION_PATCH_V6.md` and `docs/TE2_LICENSE_INVENTORY_V6.md` for upstream provenance, the small integration patch and retained notices.

The original handoff documents describe the retained foundation. See `docs/PASS1_DELIVERY_V6.md` for its historical verification and `docs/V2_PASS2_VERIFICATION.md` for this pass.

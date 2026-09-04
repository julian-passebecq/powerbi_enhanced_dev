# Run PbiBench — integrated TE2++ first pass

PbiBench is the main application. Its Model workspace hosts the pinned TE2 2.28.0 editor **inside the same process**. The first-pass compatibility host uses WPF + WinForms on .NET Framework 4.8 so TE2's model editing and C# scripting remain available. The Core, Workspace, Git and DAX Studio services also target .NET 10. The architecture and later-pass boundaries remain those in the V6 contract.

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

## Scope and boundaries

- Seven actions are implemented: format measures, explicit SUM measures, create/use a measure table, SummarizeBy None, display folders, description templates and a Last Refresh scaffold. New calculated/refresh tables contain metadata and require an explicit subsequent data refresh.
- All new automation is local until the user saves/deploys. Native remote save/deploy paths review their model JSON or exact TMSL through an `ApprovedChangePlan` before writing. Arbitrary C# scripts remain trusted, unsandboxed TE2 code; these callbacks are not a security sandbox for user-authored code.
- DAX Studio remains a separate process. Deep timings, query plans, data preview/query execution inside PbiBench, Fabric administration, PBIR authoring and AI are later passes.
- The relationship layout is deliberately basic; table roles are inferred from relationship cardinality. DAX formatting is conservative and token-preserving, with no online formatter call.
- See `docs/BASELINE_V6.md`, `docs/TE2_INTEGRATION_PATCH_V6.md` and `docs/TE2_LICENSE_INVENTORY_V6.md` for upstream provenance, the small integration patch and retained notices.

The original handoff documents remain the implementation contract. See `docs/PASS1_DELIVERY_V6.md` for actual verification and remaining manual acceptance checks.

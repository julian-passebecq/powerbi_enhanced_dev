# PbiBench

**A Windows workbench for Power BI semantic models and PBIP/PBIR projects.**

PbiBench brings model editing, reviewed bulk changes, DAX workflows and report-source inspection into one application package. It is built in C#/.NET for engineers who work with semantic models and the files behind Power BI projects.

## What you can explore

| Workspace | Purpose |
| --- | --- |
| Model | Open BIM, PBIP, TMDL or PBIT; use the integrated Tabular Editor 2 tree, property editor and scripting tools. |
| Automate and QA | Preview affected objects and before/after values, apply local model changes with grouped undo, and run the preserved TE2 Best Practice Analyzer. |
| DAX | Edit scratch queries, format offline, save `.dax` files and open expressions or queries in standalone DAX Studio. |
| PBIP / Git | Inspect semantic/report folders, branch and changed-file status without automatic commits. |
| Report Studio | Inspect local PBIP/PBIR reports, semantic-field usage and supported report edits with reviewed diffs and durable backups. |

The main `PbiBench.exe` hosts the TE2 editor in-process through WPF and WinForms on **.NET Framework 4.8**. **Report Studio uses .NET 10 in a separate process**; Fabric Toolbox and DAX Studio also remain separate processes. See [usage and supported boundaries](README_RUN.md) before working with live models or remote services.

## Build and run on Windows

Prerequisites: **Windows, Git, and Visual Studio 2022 with .NET desktop build tools**. The build also uses the .NET 10 SDK.

From the repository root in PowerShell:

```powershell
./scripts/build-pass1.ps1 -Configuration Release -InstallSdk
```

`-InstallSdk` permits the script to install the .NET 10 SDK under your user's `LocalAppData`. The script provisions pinned TE2 source, restores packages, builds, runs product tests and an offline upstream regression subset, then runs a GUI smoke test. Logs and generated test evidence go to `artifacts/build`. Online provisioning verifies TE2 commit `75f10e331b8de0dda5c213180b9b8867b4a38191`.

```powershell
./src/PbiBench.App/bin/Release/net48/PbiBench.exe
```

Start with **Demo model** for a private working copy, then try **Model → Automate → preview**. DAX Studio is a separately installed tool; configure its executable if automatic discovery cannot find it.

For packaging, run `./scripts/package-pass1.ps1 -Configuration Release` and launch `artifacts/PbiBench/PbiBench.exe`, keeping its dependencies beside it. Offline builds can use `-Offline` with the supplied TE2 snapshot; NuGet dependencies must already be cached. Full instructions: [README_RUN.md](README_RUN.md).

## Code and evidence

| Location | What to inspect |
| --- | --- |
| [`src/`](src/) | Application and module implementation. |
| [`src/PbiBench.App/`](src/PbiBench.App/) | Main Windows application. |
| [`tests/`](tests/) and [`scripts/`](scripts/) | Automated checks, build gates and packaging. |
| [Gen-2 implementation](docs/V2_PASS1_IMPLEMENTATION.md) | Workspace capabilities and process boundaries. |
| [Pass 2 implementation](docs/V2_PASS2_IMPLEMENTATION.md) and [verification](docs/V2_PASS2_VERIFICATION.md) | Supported report operations, limitations and recorded validation. |
| [Pass 3 implementation](docs/V2_PASS3_IMPLEMENTATION.md) | Unified Home/module navigation and Design Exchange contracts. |

Verification documents record specific development passes. Run the build and relevant gates against the revision you use. Older takeover documents describe the development plan; `README_RUN.md` is the practical starting point.

## Attribution and operating boundaries

The semantic-model editor derives from **[Tabular Editor 2](https://github.com/TabularEditor/TabularEditor)**, an MIT-licensed upstream project. The documented online baseline is **TE2 2.28.0**. PbiBench adds its application shell, workspace integration, automation and supporting tools around that foundation.

Retain upstream and third-party notices. See the [baseline](docs/BASELINE_V6.md), [TE2 integration patch](docs/TE2_INTEGRATION_PATCH_V6.md) and [license inventory](docs/TE2_LICENSE_INVENTORY_V6.md) for provenance and source boundaries.

- New automation edits are local until saved/deployed; native remote model writes require a reviewed change plan.
- Arbitrary C# scripts retain TE2's trusted, unsandboxed execution model. Review scripts before running them.
- Report Studio's documented Fabric report flow retrieves a local snapshot; it does not add remote report updates. TOM and PBIR changes remain separate transactions.
- Relationship layout and offline DAX formatting have deliberate limits; see the run guide for supported behavior.

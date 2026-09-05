# Compartmentalized architecture

## Pass 3 implemented package

`PbiBench.exe` is the normal entry point. Home, the compact module rail and the project context strip organize the product. `report-studio/` and `fabric-toolbox/` remain independent processes. `components.json` records their actual versions and paths. Shared light tokens and original vectors are in the UI-only DesignSystem assembly, built separately for each runtime.

Generic discovery, Windows quoting and process handoffs belong to `PbiBench.ExternalTools` (net10/net48, no project references). DaxStudio contains only its specialist handoff and depends on ExternalTools. Report Studio and Fabric Toolbox reference ExternalTools without any DaxStudio dependency. DesignExchange reuses metadata DTOs and performs bounded offline validation; the Report Studio preview cannot generate or apply PBIR. The exact project-reference graph and declared module closure are checked against `module_catalog.json`.

See [Pass 3 implementation](../V2_PASS3_IMPLEMENTATION.md) for supported contracts and verification boundaries. The broader target family below remains architectural context, not a claim that future areas are implemented.

## Why

The repository now contains semantic editing, DAX IDE/query, data exploration, automation, Fabric, workspace sync, CLI, Agent, DataForge-related projects and prototypes. Keeping every future capability inside the same desktop process would make upstream updates and dependency changes increasingly risky.

Current useful fact: the Semantic IDE host is `net48` for stable TE2 2.28 integration, while `PbiBench.Core` and `PbiBench.Fabric` already dual-target `net10.0;net48`. This is a natural seam for process-level isolation.

## Target product family

```text
PbiBench family
|
+-- PbiBench Semantic IDE (current PbiBench.exe, net48)
|   +-- TE2/TOM model editing foundation
|   +-- DAX IDE + DAX Query
|   +-- DAX Scripts/UDF/Calendar/Perspectives/Translations
|   +-- Data Preview/Pivot/Profile
|   +-- Automation/BPA/VertiPaq/tests
|   +-- PBIP/TMDL/Git semantic workspace
|   +-- semantic-model-specific Fabric hooks
|   +-- AI Context Export entry point
|
+-- PbiBench Fabric Toolbox (new separate executable, modern .NET)
|   +-- Fabric workspace/item inventory
|   +-- OneLake/Lakehouse/Warehouse exploration
|   +-- Fabric operational diagnostics
|   +-- pipeline/job/capacity/admin tooling when added
|   +-- broad Fabric API experiments without destabilizing TE2 host
|
+-- DataForge (separate application)
|   +-- deterministic source/synthetic data
|   +-- scenarios + truth manifests
|   +-- versioned JSON bridge to Semantic IDE
|
+-- External tools
    +-- DAX Studio
    +-- Power BI Desktop
    +-- VS Code/Codex
```

## Process boundary is intentional

Do not load every modern Fabric dependency into the TE2/net48 process merely for a single-window illusion.

A top-level app switcher may launch separate executables. Separate processes are acceptable and desirable when they isolate:
- framework/runtime versions;
- authentication/dependency stacks;
- crash domains;
- release cadence;
- upstream update risk.

## Shared code rules

Use shared libraries only for stable, UI-independent contracts/services.

Recommended ownership:

- `PbiBench.Core`: versioned DTOs/contracts, no UI, dual-target where practical.
- `PbiBench.Semantic`: model/TOM semantic services.
- `PbiBench.Dax.LanguageService`: DAX language intelligence.
- `PbiBench.Fabric`: Fabric API/auth/SQL service adapters, dual-target where practical.
- `PbiBench.Workspace`: filesystem/PBIP/TMDL workspace primitives.
- `PbiBench.Git`: Git adapters.
- `PbiBench.ModelEditor`: TE2 host-specific integration.
- `PbiBench.App`: Semantic IDE UI only.
- `PbiBench.FabricToolbox`: Fabric Toolbox UI only.

Avoid a Fabric Toolbox UI referencing `PbiBench.ModelEditor` or TE2 assemblies.

## Cross-app handoff

Prefer versioned files/JSON or explicit process arguments instead of runtime DLL coupling across apps.

Example handoff envelope:

```json
{
  "schemaVersion": 1,
  "kind": "FabricSelection",
  "workspaceId": "...",
  "itemId": "...",
  "itemType": "Lakehouse",
  "displayName": "Sales Lakehouse",
  "requestedAction": "OpenAsSemanticSource"
}
```

No credentials in handoff files.

## Future candidate sub-apps

Do not create these until scope justifies them, but keep boundaries possible:
- Report Engineering / PBIR Toolbox
- Deployment/CI console
- Data Quality/Testing lab

A feature belongs in Semantic IDE only when it materially helps author, understand, test, optimize or deploy a semantic model.


## C# automation boundary

C# editor intelligence is a Semantic IDE capability, but it should be isolated from both the TE2 host and the WPF shell:

```text
PbiBench.App (editor adapter)
        |
        v
PbiBench.CSharp.LanguageService
        |
        +-- completion/signatures/diagnostics/risk hints
        +-- no UI ownership
        +-- no execution authority

Execution remains:
Safe Preview -> PbiBench typed recipe/detached TOM
Trusted C#   -> existing TE2 scripting boundary
```

Do not let an editor package become the owner of semantic mutations. This separation allows the language service to update independently and makes portable tests possible.

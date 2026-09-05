# PbiBench V2 architecture

```text
PbiBench Apps / Tools
|
+-- Semantic IDE             net48
|   +-- Semantic View
|   +-- DAX Workbench
|   +-- Explore
|   +-- C# Automation Gallery
|   +-- Quality / Optimize
|   `-- Workspace / Git
|
+-- Report Studio            modern .NET separate process
|   +-- PBIP/PBIR
|   +-- Wireframe
|   +-- Inspector
|   +-- Report Actions
|   +-- Validation / diff
|   `-- semantic/report lineage
|
+-- Fabric Toolbox           modern .NET separate process
|   +-- workspaces/items
|   +-- OneLake/data
|   +-- jobs/activity
|   `-- remote report sync/deploy later
|
`-- External Tool Hub
    +-- DAX Studio
    +-- Bravo
    +-- Power BI Desktop
    `-- VS Code
```

## Ownership

Semantic IDE:
- local/offline/live semantic model authoring.

Report Studio:
- local PBIP/PBIR report engineering.

Fabric Toolbox:
- Microsoft Fabric auth/API/platform operations.

External bridges:
- process launch only; no copied external tool runtime.

## Cross-process contracts

Use versioned path/ID DTOs.
No credentials in handoffs.

## Update lanes

Keep separate:
- TE2
- semantic shell
- DAX language
- C# language/host
- PBIP/PBIR
- Report Studio
- lineage
- workspace/Git
- Fabric services
- Fabric Toolbox
- DAX Studio bridge
- Bravo bridge
- Power BI Desktop bridge
- VS Code bridge

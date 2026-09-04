# Codex Master Prompt V5 — Fabric Control Plane + Semantic IDE

You are implementing PbiBench, a private Windows-first Power BI/Fabric engineering IDE and control plane.

## Product role
PbiBench should directly perform operations when a supported, stable public interface exists. It should route to specialist tools only when they are materially better.

### PbiBench performs directly
- PBIP/TMDL/PBIR inventory and source engineering
- semantic model editing through modernized TE2/TOM/TMDL engine
- Fabric workspace/item inventory
- Fabric item definition pull/push where supported
- semantic model definition TMDL pull/push
- Power BI/Fabric REST management
- XMLA/TOM semantic model management
- BPA and typed bulk actions
- DAX tests/basic queries
- Git semantic diff / CI validation
- report definition QA
- Fabric estate/admin monitoring
- DataForge truth validation
- deployment planning

### Keep specialist/external
- DAX Studio for advanced Server Timings/query plans/VPAX investigations
- Power BI Desktop for final rendered report interactions and unsupported Desktop-only edits
- VS Code for raw source work when better

## Transport router
Never hard-code one management interface for all operations.

Implement a capability router over:
1. Local PBIP/TMDL/PBIR filesystem
2. Power BI Desktop local XMLA endpoint
3. Fabric/Power BI XMLA endpoint
4. Fabric Core REST API
5. Fabric SemanticModel definition REST API
6. Power BI REST API / Microsoft.PowerBI.Api adapter
7. Fabric Admin REST API
8. Azure Resource Manager for Fabric capacities
9. external Modeling MCP / Fabric Core MCP
10. DAX Studio/dscmd external process

Every operation declares which transports can perform it and which transport is preferred for the current context.

## Fabric REST behavior
Base URL: `https://api.fabric.microsoft.com/v1/`

Build support for:
- workspaces list/get/create/update/delete later
- items list/get
- get/update item definition
- semantic model get/update definition with TMDL/TMSL
- long-running operations
- 429 retry behavior
- permissions later
- folders later
- connections/lineage later

Read-only first.

## Power BI REST behavior
Base URL: `https://api.powerbi.com/v1.0/myorg/`

Use raw REST in the initial architecture behind interfaces. Add the official `Microsoft.PowerBI.Api` SDK adapter after current package/version validation.

Initial uses:
- workspaces/groups
- reports
- semantic models/datasets
- data sources
- refreshes
- admin inventory when authorized.

## XMLA/TOM
XMLA is the preferred live semantic-model engineering interface when read/write XMLA is enabled and the operation is model metadata/data specific.

Use it for:
- measures
- relationships
- calculation groups
- perspectives/translations
- partitions
- OLS/RLS metadata
- partial metadata deployment
- DAX queries/DMVs
- table/partition refresh
- tracing/performance integrations.

Do not use REST definition replacement for a one-measure edit if XMLA/TOM is the safer precise operation.

## Definition pull/push
Cloud definitions are code artifacts.

Flow:
```text
Fabric semantic model
 -> getDefinition(format=TMDL)
 -> decode parts
 -> canonical local snapshot
 -> TE2/TMDL analysis
 -> Git/object diff
 -> plan
 -> approval
 -> updateDefinition
 -> LRO wait
 -> re-pull definition
 -> semantic validation
```

Preserve `.platform` handling and treat metadata updates separately.

## DAX Studio boundary
PbiBench includes DAX Lab Light for normal queries/tests, but DAX Studio remains separate.

Build:
- locate/configure `daxstudio.exe`
- launch with `--server`, `--database`, `--file`
- configure `dscmd.exe`
- run CSV/JSON/XLSX queries
- run benchmark
- generate VPAX
- authenticate via DAX Studio only when user explicitly chooses that path.

Never copy Ms-RL DAX Studio source into MIT/permissive PbiBench files.

## TE2 modernization
Use TE2 as semantic foundation under license rules.
Do not build final UI as a patchwork of WinForms forms.

Priority TE2-derived concepts/components:
- object model wrappers
- BPA engine
- dependency traversal
- scripting context
- batch edits
- undo concepts.

Add characterization tests before behavioral changes.

## Cloud admin/estate
Create an Estate surface:
- tenant/workspace/item inventory
- workspace role assignments
- semantic model/report inventory
- refresh failures
- Git connection state when available
- capacity association
- preview admin API warnings
- FUAM/FCA links or adapters rather than rebuilding them immediately.

Fabric Admin APIs that are preview must be clearly labeled and disabled for destructive writes by default.

## Auth
Initial auth modes:
- interactive delegated user
- service principal later
- managed identity later for hosted/automation mode.

Never ask the user to paste tokens into UI text boxes.
Use MSAL.NET/appropriate identity libraries in the implementation phase after pinning stable package versions.

## Acceptance of V5 foundation
A demo is successful when:
1. local PBIP scanned,
2. Fabric workspaces/items enumerated,
3. semantic model TMDL pulled,
4. local/cloud TMDL diff shown,
5. model inventory shown through TE2/TOM layer or deterministic parser,
6. BPA read-only results shown,
7. DAX Studio launch works,
8. no write occurs without approval/snapshot,
9. audit journal records every remote operation.

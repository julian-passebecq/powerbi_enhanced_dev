# Fabric Toolbox V0.2 functional scope

Current Toolbox already has a separate process, workspaces, all-item inventory, Lakehouse/Warehouse/data browsing, schemas/tables/columns, bounded SQL preview, selection handoff and in-memory auth.

Continue functionality here.

## Add now — read-only platform operations/inventory

1. Better workspace/item explorer
- search/filter items by name/type;
- item details;
- copy stable IDs;
- bounded JSON/CSV inventory export;
- no credentials/tokens in exports.

2. Recent job/operation instances
Use only verified documented public Microsoft Fabric APIs.
For supported items show:
- item;
- job type;
- status;
- start/end;
- duration;
- public failure/status summary;
- job/correlation instance ID.

Requirements:
- bounded pagination;
- cancellation;
- manual refresh;
- no start/cancel/retry writes in V0.2;
- unsupported types shown explicitly.

3. Make Operations page real
- recent operations grid;
- filters;
- Refresh;
- item linkage;
- details panel.

Opening it must not run/mutate anything.

4. Keep semantic editing outside Toolbox
No measures/DAX/TOM/TE2 scripting/model Undo/PBIP semantic editing in Toolbox.

## Future, not forbidden
Lineage, capacities, governance/permissions inventory, pipeline execution, maintenance helpers and deployment tooling may be added in later Fabric-lane versions.

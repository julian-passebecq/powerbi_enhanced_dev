# Fabric Control Plane V5

## Supported control surfaces

### Fabric Core REST API
Base: `https://api.fabric.microsoft.com/v1/`

Use for workspace/item lifecycle and definitions.

Initial read-only:
- list workspaces
- list items
- get item
- get item definition
- get semantic model definition TMDL/TMSL.

Write phase:
- update item definition
- update semantic model definition
- create/update selected supported items
- permissions/folders only after explicit approval policy.

### Fabric Admin API
Base: `https://api.fabric.microsoft.com/v1/admin/`

Use for tenant/estate inventory when the user has admin rights.
Some new Admin endpoints are Preview: show a Preview badge and do not make them required for core functionality.

### Fabric Core MCP
Remote preview endpoint:
`https://api.fabric.microsoft.com/v1/mcp/core`

Use as an optional agent transport for search/workspaces/items/permissions/folders/capacities.
Do not make the desktop app depend on MCP for deterministic operations already covered by REST.

### Azure Resource Manager
Use ARM/`Azure.ResourceManager.Fabric` for Fabric capacity lifecycle and SKU/suspend/resume controls.
Capacity mutation is high-risk and requires a separate approval level.

## Item definition behavior
`Get Item Definition` and `Update Item Definition` support LROs.
Definitions consist of file-like parts with path + InlineBase64 payload.
The `.platform` part is metadata and must be treated carefully; metadata update is explicit.

## Semantic model definition
Default cloud semantic definition format should be TMDL.
Support TMSL only where there is a concrete reason.

PbiBench should persist a canonical snapshot:
```text
.pbibench/cloud-snapshots/
  <workspace-id>/
    <item-id>/
      <timestamp>/
        .platform
        definition.pbism
        definition/
          model.tmdl
          relationships.tmdl
          tables/*.tmdl
```

No credentials/tokens in snapshots.

## LRO client
Implement one reusable long-running operation poller:
- detect 202
- read `Location`
- respect `Retry-After`
- cancellation
- max wait policy
- structured operation ID
- final result/error
- audit timing.

## Rate limits
Implement generic retry policies for 429 using server-provided Retry-After.
Do not spin or hard-code aggressive retry loops.

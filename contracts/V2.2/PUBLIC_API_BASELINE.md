# Public API baseline — 2026-09-05

Microsoft Learn currently documents:

Report Get Definition:
`POST https://api.fabric.microsoft.com/v1/workspaces/{workspaceId}/reports/{reportId}/getDefinition`

Responses include:
- 200 ReportDefinitionResponse;
- 202 LRO with Location, x-ms-operation-id, Retry-After;
- 429 Retry-After.

Report definition parts may include:
- `StaticResources/`;
- `definition/` for PBIR;
- `definition.pbir`;
- optional `semanticModelDiagramLayout.json`;
- PBIR-Legacy `report.json` when applicable.

Current PBIR project docs show `definition.pbir` version `4.0` as the enhanced report example and state PBIR is publicly documented JSON intended for programmatic modification.

Codex must verify the current Microsoft page before relying on any new property/endpoint shape.

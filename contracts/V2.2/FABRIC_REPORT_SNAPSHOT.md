# Fabric Toolbox — read-only report snapshot

Use Microsoft Fabric's current report API:

`POST /v1/workspaces/{workspaceId}/reports/{reportId}/getDefinition`

This is a read operation despite HTTP POST.

Handle:
- 200 direct definition;
- 202 LRO with Location / x-ms-operation-id / Retry-After;
- 429 Retry-After.

## Flow
1. User selects a Fabric Report.
2. Explicit `Get report definition`.
3. Fabric Toolbox retrieves the public definition.
4. Strictly decode InlineBase64 parts.
5. Write to a NEW local snapshot directory.
6. Write manifest: workspace/report IDs, retrieval time, format, part hashes.
7. Open snapshot in Report Studio.

Report Studio receives files/IDs only, never Fabric credentials.

## Path/payload safety
Reject:
- absolute paths;
- `..`;
- duplicate normalized paths;
- reserved/device paths;
- reparse targets;
- unsupported payload type;
- excessive per-part/aggregate size;
- overwrite of existing user files.

Microsoft currently documents report definitions as PBIR or PBIR-Legacy. PBIR-Legacy stays read-only in Report Studio.

No `updateDefinition` in Pass 2. Remote writes belong to a later reviewed Fabric pass.

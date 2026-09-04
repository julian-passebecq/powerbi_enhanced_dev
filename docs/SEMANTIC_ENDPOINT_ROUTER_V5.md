# Semantic Endpoint Router V5

## Available model transports

### Local PBIP/TMDL
Best for:
- source-controlled development
- deterministic file diffs
- code review/CI.

### Power BI Desktop XMLA
Best for:
- live local model inspection
- external-tool workflow
- precise model operations before save.

### Fabric/Power BI XMLA
Best for:
- advanced remote semantic model edits
- partial/metadata-only changes
- partitions
- OLS/RLS/perspectives/translations
- DAX/DMV/tracing.

### Fabric semantic definition REST
Best for:
- whole-definition snapshot/import/export
- TMDL pull/push
- migrations/deployment pipelines around source artifacts.

### Power BI Modeling MCP
Best for:
- agent-driven semantic model interactions when preview feature use is acceptable.
- remain optional/external.

## Selection examples

| Intent | Preferred |
|---|---|
| Change one measure in live Fabric model | XMLA/TOM |
| Pull whole model into Git | Fabric `getDefinition(TMDL)` |
| Compare PBIP model with deployed model | local TMDL + REST TMDL snapshot |
| Manage partition refresh | XMLA/TOM |
| Agent explores model safely | Modeling MCP read-only or TOM inventory |
| CI validates source model | local TMDL/TOM parser |

## Capability object
Each adapter reports capabilities at runtime rather than assuming access:
- connected
- read metadata
- write metadata
- query DAX
- refresh
- get definition
- update definition
- admin inventory
- preview flag
- required permissions.

# CI, semantic catalog and catalog corrections

## Hosted CI

Replace/rename the old V11-centric workflows with product-neutral names, preferably:
- `pbibench-fast.yml`
- `pbibench-release-windows.yml`

Fast:
- V11 portable net10 regression;
- V2 tests net10;
- Report Studio build;
- Fabric Toolbox build/tests;
- Report Studio output isolation;
- Fabric Toolbox output isolation.

Release Windows:
- V11 net10/net48 portable regression;
- V2 Release tests;
- Report Studio Release build + reliable offline test/smoke if possible;
- Fabric Toolbox Release build/tests;
- isolation checks;
- upload TRX/evidence.

No hosted claim for live TE2/Desktop/XMLA/Fabric.

## Semantic catalog fix

Do not use one-character indentation as a TMDL nesting rule.

Preferred:
1. reuse an existing pure semantic/TMDL metadata service if available;
2. otherwise implement a robust declaration reader based on relative indentation blocks;
3. add an immutable SemanticCatalogSnapshot DTO that Semantic IDE can generate from loaded metadata and Report Studio can consume without TE2.

Snapshot excludes credentials, connections and partition/source expressions.

Tests:
- 1/2/4 spaces;
- tabs;
- quoted names;
- nested metadata;
- next table boundary;
- partial unsupported layout;
- completeness never overstated.

## Catalog corrections

- remove stale `future-report-tooling` now that `report-studio` exists, or rename it only for genuinely future report extensions;
- update feature rows to use actual `pbir`, `report-studio`, `lineage` modules;
- update V2 Pass1 verification with post-push hosted success while noting V2 was not yet hosted-tested.

## Dependency policy
Do not opportunistically update TE2, MSAL, SqlClient, FastColoredTextBox or JsonSchema.Net.

## PBIR version compatibility
Replace the permanent `report.Version == "4.0"` mutation gate with a small supported-version policy tied to the pinned Microsoft schema bundle.
- current known 4.0 remains supported;
- a future 4.x is not automatically trusted merely because it is >=4;
- if all required referenced schemas/version contracts are pinned and explicitly listed as supported, writes may be enabled;
- otherwise browse read-only and explain which schema/version lane must be updated.

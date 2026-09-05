# Independent audit — V2 Pass 1

Audited SHA: `1e02628f7b35af0e5b92c0452f86d3b102562cc2`

## Green areas

Pass 1 is substantive:
- Semantic View evolves existing DiagramView instead of duplicating it.
- DAX Workbench adds model explorer/context help while keeping current query engine.
- C# Gallery contains 13 curated entries: 9 Safe Recipes + 4 native routes.
- Report Studio is a separate .NET 10 WPF process.
- `PbiBench.Pbir` provides immutable report indexing, validation, lineage and reviewed change plans.
- Report Studio has tree, wireframe, inspector, raw JSON, validation, lineage and typed actions.
- PBIR writes use exact hashes, review IDs, durable backups, atomic per-file writes, post-write validation and guarded rollback.
- unknown/incompatible schemas block mutation.
- `.pbi`, `.pbibench`, `.git`, caches and path escapes are excluded/rejected.
- Bravo/Desktop/VS Code/Report Studio handoffs are context-aware.

Codex local Release gate reported:
- solution 0 warnings/errors;
- 347 impacted tests;
- Semantic IDE smoke: 51 checks;
- Report Studio smoke: 10 checks.

Both hosted GitHub workflows also completed successfully after push.

## Audit gaps

### 1. Hosted CI does not test Gen-2
Current hosted workflows are still V11-named. They run V11 net10/net48 tests plus Fabric Toolbox, but not `PbiBench.V2.Tests` or Report Studio build/isolation.

### 2. TMDL indentation bug
`ReportLineage.ReadLocalModelAsync` accepts column/measure declarations only when `indent == tableIndent + 1`.
Valid TMDL may use several spaces per indentation level, causing false partial/unverified catalogs.

Prefer an existing semantic/TMDL metadata service or a versioned immutable semantic catalog snapshot. Report Studio must stay TE2-free.

### 3. Gallery provenance blur
Every PowerBiGallery card currently carries TabularEditor/Scripts as its reference/source metadata, including PbiBench-native BPA/Profile/Measure Table routes.
Split implementation origin from optional reference source.

### 4. Stale module
`future-report-tooling` remains in module/feature catalogs even though Report Studio is now implemented.

### 5. Verification doc predates hosted result
Update V2_PASS1_VERIFICATION.md to record the successful hosted runs, but explicitly state that those hosted runs did NOT yet exercise V2 tests.

### 6. PBIR version handling is too literal
`ReportValidator` currently rejects every `definition.pbir` version except exactly `4.0`.
Current Microsoft documentation describes enhanced PBIR as `4.0 or higher`.
Keep fail-closed behavior, but derive write compatibility from the pinned schema/version bundle rather than a permanent `== "4.0"` check. Unknown future versions must remain read-only until their referenced schemas are pinned and regression-tested.

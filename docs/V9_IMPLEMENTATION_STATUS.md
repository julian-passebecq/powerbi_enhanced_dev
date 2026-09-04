# V9 implementation status

V7 prerequisite: **functional/structural gate complete**, recorded in `V7_FUNCTIONAL_GATE.md`. Preserved portable build: `artifacts/PbiBench-V7-20260905/`; its independent packaging launch check also passed all 15 checks. Taskbar icon appearance and 100/125/150% pixel-level DPI appearance remain manual visual QA pending and do not block implementation, per the user.

| Milestone | Status |
|---|---|
| V9.1 DAX IDE Core | Functional implementation and automated gate complete; live data validation remains pending an accessible catalog |
| V9.2 Data Exploration | Functional implementation and automated gate complete; populated-engine validation remains pending |
| V9.3 Model Authoring Pro | In implementation and verification |
| V9.4 Automation / QA / Optimization | Not started |
| V9.5 Fabric / Refresh / Workspace | Not started |
| V9.6 CLI / Agent / Compiler | Not started |

Milestones are implemented in this order. The final `contracts/V9/ACCEPTANCE_GATE_V9.md` remains required.

## V9.1 verification

`scripts/build-pass1.ps1 -Configuration Release -Offline` passed on 2026-09-05 with 0 warnings and 0 errors. Test executions: adapters 39 net10 + 43 net48; application 4 net48; language service 34 net10 + 34 net48; semantic 40 net48; upstream TOM 266; upstream scripts/parser/tree/formatter 26. **486 passed, 0 failed, 0 skipped.** All **21 launch checks** passed, including the original 15 V6/V7 checks.

Evidence: `artifacts/v9-1-build-verification.log`; launch report and generated captures in `artifacts/build/smoke-Release-fd8819724701460db65cccc9798db60b/`. The DAX capture was inspected from the generated image file. These are automated rendering checks, not Windows taskbar or DPI visual QA.

The language-service large-model fixture contains 20,000 metadata symbols: analysis plus completion took 31 ms on net10 and 27 ms on net48, with 400 visible suggestions. Framework-specific reports: `artifacts/v9-language-tests/`.

Real WPF tests verify cancellation, stale results, document recovery, and successful results surviving failed history writes. Real filesystem tests verify atomic history replacement retries a temporarily locked destination and cancellation preserves the preceding file. Query transport tests use an injected independent session to verify cancellation, multiple results, limits and credential handling.

Read-only probes connected to the local Analysis Services endpoints but found no visible catalog. Therefore successful execution against a populated engine, real-engine query semantics, and UDF-engine behavior are **not demonstrated by these tests**. Fixture result grids are explicitly labeled as fixtures. The implemented public TOM transport remains available for a connected accessible model; this integration limitation is retained for the final V9 report.

## V9.2 verification

The full Release build script passed with 0 warnings/errors, **581 test executions** (84 + 88 adapters, 8 WPF, 34 + 34 language, 41 semantic, 266 + 26 upstream), and **25 launch checks**. The preceding suites remain present. Evidence: `artifacts/v9-2-build-verification.log`, preserved TRX copies in `artifacts/v9-2-functional-evidence/`, and `artifacts/build/smoke-Release-c0716540e881466297ad861e4bff4a3f/`. Generated Data Preview and Pivot captures were inspected from files.

The Data page hosts independent table previews, cancellable engine profiles, relationship coverage, and Pivot Lab. Native table context menus open Preview Data. Import paging requires an explicit zero-row WINDOW capability probe and complete key uniqueness check; connection/model changes invalidate proof. Other cases show first-N and source/Direct Lake cost notes. Sorting/filtering is generated with schema-resolved references and typed literals. Result grids virtualize rows/columns and support copy/CSV.

Pivot supports drag/drop Rows/Columns/Values/Filters, engine totals, auto refresh, JSON layouts and regression artifacts. Tests check that blank members and engine totals remain separate, saved ordering/row limits survive loading, and query changes/cancellation cannot replace current results with stale data. Snapshot artifacts reject truncated or mismatched results. Public metric definitions and sources are in `V9_DATA_PROFILE_REFERENCE.md`; Pivot behavior and sources are in `src/PbiBench.Core/DataExploration/PivotREADME.md`.

All populated-engine paging/profile/Pivot results remain integration validation pending because no catalog is available. The launch Pivot data is explicitly a fixture. No model or source data was deployed to manufacture integration evidence.

## V9.1 boundaries and sources

The new DAX language service is original code over immutable metadata snapshots. The MIT TE2 grammar supplies open built-in function identifiers with its license preserved; tolerant source-span tokenization handles current documented UDF syntax independently. Original diagnostics are editor assistance, not a replacement for Microsoft engine validation.

Query execution owns a separate TOM session and does not reuse or cancel the hosted model editor connection. Display limits bound retained result rows and cells, not engine work. Query files use the semantic model's `DAXQueries` folder when that project context is unambiguous. DAX Studio remains a standalone deep-analysis tool.

Public behavior/API references:

- [Microsoft DAX queries](https://learn.microsoft.com/en-us/dax/dax-queries): DEFINE scope, multiple EVALUATE statements, and ordering clauses.
- [Microsoft FUNCTION statement](https://learn.microsoft.com/en-us/dax/function-statement-dax): UDF parameter types, passing modes, defaults, and lambda body syntax.
- [Microsoft DAX query view](https://learn.microsoft.com/en-us/power-bi/transform-model/dax-query-view): query files under the semantic model/report DAXQueries folder.
- [Microsoft AmoDataReader.NextResult](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.amodatareader.nextresult?view=analysisservices-dotnet): multiple rowsets through the public reader.
- [Microsoft Server.CancelCommand](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.core.server.cancelcommand?view=analysisservices-dotnet): cancellation of the independent query session.

No proprietary TE3 source, assets, or decompilation are used.

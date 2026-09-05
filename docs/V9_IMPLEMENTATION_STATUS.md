# V9 implementation status

V7 prerequisite: **functional/structural gate complete**, recorded in `V7_FUNCTIONAL_GATE.md`. Preserved portable build: `artifacts/PbiBench-V7-20260905/`; its independent packaging launch check also passed all 15 checks. Taskbar icon appearance and 100/125/150% pixel-level DPI appearance remain manual visual QA pending and do not block implementation, per the user.

| Milestone | Status |
|---|---|
| V9.1 DAX IDE Core | Functional implementation and automated gate complete; live data validation remains pending an accessible catalog |
| V9.2 Data Exploration | Functional implementation and automated gate complete; populated-engine validation remains pending |
| V9.3 Model Authoring Pro | Functional implementation and automated gate complete; populated-engine UDF/calendar validation remains pending |
| V9.4 Automation / QA / Optimization | Functional implementation and automated gate complete; populated-engine metrics and assertion validation remain pending |
| V9.5 Fabric / Refresh / Workspace | Functional implementation and automated gate complete; live Fabric, refresh and workspace writes remain integration validation pending |
| V9.6 CLI / Agent / Compiler | Functional implementation, automated gate and portable package verification complete; optional live provider validation remains pending |

All six milestones were implemented in this order. Work stops at `contracts/V9/ACCEPTANCE_GATE_V9.md`; the evidence, demonstrated scope and remaining external validation are recorded in `V9_ACCEPTANCE_REPORT.md`.

Build warning scope: the zero-warning counts below refer to the PbiBench solution build. The pinned upstream TE2 build retains its existing MSBuild reference-resolution warnings for Microsoft.Identity.Client.NativeInterop and Antlr4.Runtime. These appear in the milestone logs; upstream builds, runtime checks and configured tests succeed. No hosting/runtime redesign was made to remove these warnings.

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

## V9.3 verification

The full Release build script passed on 2026-09-05 with **0 build warnings/errors, 715 test executions and 36 launch checks**. Counts: 89 net10 + 95 net48 adapters, 9 WPF, 74 + 74 language, 82 semantic, and the preserved 266 + 26 upstream tests. No tests failed or were skipped. Evidence: `artifacts/v9-3-build-verification.log`, preserved reports in `artifacts/v9-3-functional-evidence/`, and `artifacts/build/smoke-Release-4ee78079e20a42d89eed5732621c14e9/`.

The launch suite exercises the actual native expression buffer, entering Model tools, DAX Scripts preview/apply/undo, compatibility guards, editable perspective/translation matrices and table-group annotations. Generated UDF, Calendar, DAX Scripts, Perspective, Translation and Diagram captures were inspected from files. These do not replace Windows taskbar or DPI visual QA.

The shared preview tests cover stale and foreign model sessions, postcondition failures, Undo/Redo, failures after TE2 commits an undo batch, and preservation of unrelated undo history. No vendor or hosting architecture changes were required. DAX code-action tests verify conservative evaluation-context boundaries and stable source versions; native navigation distinguishes same-named columns by model identity.

The WPF large-grid fixture displays 10,000 rows × 12 columns: the scoped measurement was 1,272 ms to materialize and lay out, with 27 realized rows. This is an automated fixture measurement on this machine, not engine query performance. UDF and calendar metadata tests use supported local compatibility levels; successful execution against a populated engine remains unverified because no accessible catalog was available. Behavior, sources and limits are documented in `V9_MODEL_AUTHORING_REFERENCE.md`, `V9_DAX_AUTHORING_REFERENCE.md` and `V9_DIAGRAM_AUTHORING_REFERENCE.md`.

## V9.4 verification

The complete Release build/test/launch script passed on 2026-09-05: **906 test executions, 47 launch checks, no failed or skipped tests**. Counts: 168 net10 + 176 net48 adapters, 17 WPF, 74 + 74 language, 105 semantic, and the preserved 266 + 26 upstream tests. The PbiBench build reports 0 warnings and 0 errors; the existing upstream reference warnings are described above. Evidence: `artifacts/v9-4-build-verification.log`, preserved TRX files and machine-readable counts in `artifacts/v9-4-functional-evidence/`, and `artifacts/build/smoke-Release-afd7809fd8ef452aa0c56074c6c555ce/`.

Automation now has detached Safe C# Preview, a separately acknowledged Trusted Legacy workflow, typed action recording/replay and a persisted macro library. Safe Preview interprets only the documented model-edit subset and shows exact before/after rows before the existing guarded apply/Undo transaction. Direct STA tests execute benign scripts through the actual TE2 compiler and verify trust, snapshots, stale/consumed tickets, compile/runtime errors, Console restoration and native Undo. Native model tests share a nonparallel runtime collection to respect TE2's process-global model state.

QA includes eight original, versioned BPA packs; editable rule preferences and source-aware suppression; VPAX import and fixed public-DMV capture; optimization findings and bounded A/B evidence; typed DAX assertions and reviewed snapshots. The shared queue keeps both direct and caller-token cancellation callbacks off the UI thread. Tests reject mismatched/stale query evidence, incomplete metrics, numeric underflow/precision errors and invalid recipe input. The launch suite displays an actual detached script diff, applies and undoes it, reloads a recorded recipe, and exercises the real WPF result/task/grid surfaces with explicitly labeled fixtures.

Generated script preview, BPA packs, semantic tests and VertiPaq captures were inspected from local files. Smoke settings, macros and snapshots use the isolated profile. These captures do not replace the two pending V7 manual visual checks. A populated engine remains unavailable, so successful live DMV captures, assertions and benchmarks are not claimed. Sources and precise limitations are in `V9_SCRIPT_AUTOMATION_REFERENCE.md`, `V9_BPA_RULE_PACKS.md`, `V9_VERTIPAQ_REFERENCE.md` and `V9_SEMANTIC_TESTS_REFERENCE.md`.

## V9.5 verification

The full Release build/test/launch script passed on 2026-09-05 with **1,101 test executions and 56 launch checks**, no failures or skips. Counts: 240 net10 + 248 net48 adapters, 33 WPF, 74 + 74 language, 140 semantic, and the preserved 266 + 26 upstream tests. The PbiBench build reports 0 warnings/errors; pinned upstream reference warnings remain. Evidence: `artifacts/v9-5-build-verification.log`, eight preserved TRX reports and counts in `artifacts/v9-5-functional-evidence/`, and `artifacts/build/smoke-Release-dcc1ac4ab59b477490d0bff7697bf5b3/`.

Fabric browsing uses public Microsoft interfaces with separate Fabric, OneLake and SQL consent. Local import, storage-mode conversion and schema updates use the existing exact preview/Undo engine. Refresh uses a private connection, immutable target metadata and reviewed TMSL. PBIP/TMDL synchronization compares disk, live and Git metadata, detects external changes, and preserves recovery snapshots before reviewed writes. All three workflows are integrated into the existing shell.

Regression tests cover native multi-partition Undo order, first-read TMDL watcher behavior, stale connection contexts, cancellation and credential handling. A transient Windows layout-file replacement lock was resolved using the existing bounded atomic-file retry helper without changing the V7 layout format or hosting architecture. Generated Fabric import, advanced refresh and workspace comparison captures were inspected from local files. They do not replace the two pending manual V7 visual checks.

Successful real Fabric authentication/import/preview, processing and remote workspace writes remain integration validation pending. No accessible populated catalog or Fabric account was available, and fixture evidence is explicitly labeled. Public API references and supported boundaries are recorded in `V9_FABRIC_REFERENCE.md`, `V9_FABRIC_AUTHORING_REFERENCE.md`, `V9_REFRESH_REFERENCE.md` and `V9_WORKSPACE_REFERENCE.md`.

## V9.6 verification and final gate

The complete Release build/test/launch script passed on 2026-09-05 with **1,276 test executions, 68 application launch checks and 25 CLI launch checks**, no failed or skipped tests. Counts: 302 net10 + 310 net48 adapters, 42 WPF, 74 + 74 language, 182 semantic, and the preserved 266 + 26 upstream tests. The PbiBench build reports 0 warnings/errors. Evidence: `artifacts/v9-6-build-verification.log`, eight preserved TRX reports and counts in `artifacts/v9-6-functional-evidence/`, application captures in `artifacts/build/smoke-Release-d4c73eb307ce406a9db7b7259401da06/`, and CLI JSON/stderr evidence in `artifacts/build/cli-smoke-Release-d72d5720b2054b6b8688b33c79baca85/`.

The own CLI now uses the same typed semantic command services as Agent/local GUI operations. Separate-process launch checks cover reads, BPA thresholds, TOM/TMDL validation, semantic diffs, profiles, Unicode/quoted values, property/script/gallery previews and application, stale/forged/replayed approval rejection and missing remote targets. Native fixtures cover independent query, refresh/deployment sessions, recovery files and uncertain remote outcomes. Saved approval hashes bind exact changes, target, source/destination state, nonce and expiry; state is isolated in smoke runs. Inline authentication in CLI refresh source overrides is rejected before capture. Displayed deployment source values redact credential-bearing fields and multiline expressions while hashes bind the exact originals.

Agent starts offline, projects explicitly selected context, validates a strict proposal schema, and uses the shared native review/apply/Undo engine. Query and test proposals stage real DAX/QA drafts. Staging a test appends without discarding existing tests or unsaved drafts. Optional OpenAI Responses requests have no automatic tool execution. Tests cover cancellation, stale model/selection/provider responses and explicit sharing before HTTP access. Generated Agent, compiler and package captures were inspected from local files.

The compiler and local package prototypes are bounded and labeled. A native package regression exposed TE2 Function deletion Undo changing the order of unrelated functions. The separate reproducible `te2-2.28.0-function-undo-order.patch` corrects only Function collection restoration, retaining wrapper identities and complete metadata. Interleaved install/update/remove Undo/Redo tests pass. The pinned source and hosting architecture remain intact; both integration patches and notices ship in the package.

Portable output `artifacts/PbiBench-V9-20260905/` independently passed **68 application + 25 CLI checks**; all **624 manifest file hashes** were verified. Packaging evidence is `artifacts/v9-package-verification.log`, `artifacts/package-smoke-6817eefe03804ee6901304f488d1b6ae/`, `artifacts/package-cli-smoke-8512d07e11d147f397d15ca4cc0a8ea7/`, and the preserved package report in `artifacts/v9-6-functional-evidence/`. The packaged CLI also passed the four-step read-only CI workflow in `artifacts/v9-6-ci-final/`.

The automated functional gate is complete. Full external integration acceptance is not claimed: the populated-engine, Fabric and provider checks described in `V9_ACCEPTANCE_REPORT.md` remain pending, as do the two nonblocking V7 manual appearance checks. No implementation beyond the V9 acceptance gate was started.

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

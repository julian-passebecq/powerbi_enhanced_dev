# PbiBench Pass 1 delivery — 2026-09-04

## Implemented product

`PbiBench.exe` starts a WPF workbench with the **actual TE2 2.28.0 Model Editor hosted in the same process**. It retains the upstream tree, multi-selection, property and expression editing, object commands, dependency navigation, undo/redo, C# scripting, BPA, perspectives and translations. No wholesale TE2 UI rewrite or cloud/report/AI implementation was undertaken.

The compatibility host targets .NET Framework 4.8 to preserve working WinForms and C# scripting. Core, Workspace, Git and DAX Studio also build for .NET 10. This is an incremental compatibility implementation within the V6 module boundaries, not a replacement architecture.

Added workspaces and behavior:

- Shell navigation, current model/connection, Git status, selection inspector, validation/output and keyboard workspace commands.
- Seven typed Automation actions with exact before/after previews, explicit apply, model fingerprint checks, validation, grouped TE2 undo and failure rollback.
- BPA companion: rule/severity/object/reason/source, proposed before/after changes, conservative fix previews and object navigation; full upstream BPA remains usable.
- Highlighted DAX scratch editor, local token-preserving formatter, query save, active-expression conversion, DAX Studio discovery/configuration and exact context launch. No query is automatically executed.
- Relationship diagram with table-role inference, cardinality, active/inactive styling, filter arrows and click-to-select.
- PBIP ancestry, semantic folders, TMDL/PBIR indicators, Git branch/dirty/semantic changes, conflict and pending Desktop-change warnings.
- Explicit review on the normal TE2 remote SaveDB/deployment paths, using a host approval callback and `ApprovedChangePlan`. The deployment preview shows the actual generated TMSL; it does not claim to have loaded the destination's original metadata.
- Safe application lifecycle: pending expression text participates in dirty checks; canceled close/open/new preserves edits; native Exit routes through the shell. Smoke tests use isolated PbiBench/TE2 profiles.

## Verified

`scripts/build-pass1.ps1 -Configuration Release` passed on this Windows machine. The PbiBench solution built with **zero warnings and zero errors**. Upstream build warnings are separately recorded as baseline behavior.

| Verification | Result |
|---|---:|
| Adapter tests, .NET 10 | 31 passed |
| Adapter and actual hosted-editor tests, .NET Framework 4.8 | 32 passed |
| Semantic/Automation tests, real TE2 model wrapper | 16 passed |
| Upstream TOM property/undo + remote-review hook tests | 266 passed |
| Upstream script/parser/tree-path/formatter subset | 26 passed |
| **Final automated test executions** | **371 passed, 0 failed** |
| Application launch smoke | 15 checks passed |

The final host/profile change was rebuilt and the affected 63 adapter executions and 15 launch checks were repeated successfully. Before integration, the unmodified bundled and pinned upstream baselines each passed 288 tests; these baseline executions are additional to the final table above.

The launch test opens a real BIM fixture, checks the populated native tree and selected expression, edits a property with undo/redo, previews/applies/undoes an action, displays real BPA findings, follows a diagram node back to the tree, converts/formats DAX, and saves through TE2. Native BPA is also instantiated and analyzed by the actual hosted-editor integration test. The service tests cover all seven action roundtrips, stale/replayed/cross-session plans, rollback after a setter fails, unsupported selections, name collisions, escaped identifiers, graph properties and token preservation.

DAX Studio **3.0.11.982**, installed at `C:/Program Files/DAX Studio/DaxStudio.exe`, was launched with the generated `daxstudio-launch.dax`; its UI exposed that document tab and `<Not Connected>`. No query or model mutation was executed. Exact `--server`, `--database`, `--file` construction is tested through the replaceable process adapter and Windows command-line parsing.

## Evidence

- `artifacts/release-verification.log`: complete Release build/test output.
- `artifacts/build/*.trx`: product and upstream test reports.
- `artifacts/final-tests/*.trx`: affected adapter tests after profile isolation.
- `artifacts/smoke-isolated-final/smoke-result.json`: final source-build launch checks.
- `artifacts/smoke-isolated-final/{model,model-compact,automation,bpa,dax,diagram}.png`: actual WPF/native-control render captures, visually inspected. Native surfaces are composited into the WPF capture to account for WinForms airspace.
- `artifacts/daxstudio-launch-evidence.json`: installed DAX Studio file-launch evidence.
- `artifacts/PbiBench/package-manifest.json`: portable runtime and notice checksums.
- `artifacts/package-smoke-*/smoke-result.json`: independently staged portable-runtime verification.

## Remaining manual acceptance checks and limits

- Live Desktop/XMLA/Fabric connection/authentication/save/deploy roundtrips were not exercised; no user endpoint or credentials were supplied. The real upstream connection UI is preserved, and remote rejection/approval seams are tested offline. No cloud write was performed.
- Normal and compact logical viewports were checked. A physical monitor at 150% scaling and per-monitor DPI transitions still need manual testing; the manifest enables DPI awareness.
- Arbitrary C# scripts remain **trusted, unsandboxed** TE2 code. Approval hooks cover the normal handler/deployer pathways; they cannot constrain arbitrary code that creates its own server/process/file APIs.
- PBIP/Git detection is read-only and tested with temporary local fixtures. It does not implement Desktop reload, semantic Git diff, automatic commits or PBIR authoring.
- The DAX scratch workspace does not execute queries locally. The formatter is conservative layout formatting, not full SQLBI formatting or semantic validation.
- Table roles are inferred; the diagram uses a basic automatic layout. Larger schemas may need scrolling. Last Refresh/new calculated-table scaffolds require a later explicit data refresh.
- Remote rollback is a separately reviewed operation; only local model changes use automatic TE2 undo. Destination metadata is not fetched solely to manufacture a before-diff for arbitrary deployment targets.

## Source and notices

Official TE2 tag `2.28.0` is pinned to `75f10e331b8de0dda5c213180b9b8867b4a38191`. The supplied snapshot remains intact. The small remote-review patch is tracked under `vendor/patches/`; provisioning applies it idempotently and supports a clearly identified offline snapshot fallback.

TE2's MIT notice and upstream/dependency notices are retained, including FastColoredTextBox, FastWildcardMatching, TreeViewAdv and ActionListWinForms notices. See `TE2_LICENSE_INVENTORY_V6.md`, `TE2_NUGET_LICENSE_INVENTORY.json` and the portable `licenses` folder. DAX Studio source is not embedded; no TE3 source/assets were used.

The implementation stops here. Next work should complete the manual live/DPI checks, then proceed to the contract's Pass 2 DAX query/results, editor completion and workspace synchronization priorities.

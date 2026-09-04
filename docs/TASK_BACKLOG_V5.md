# Full Codex Task Backlog V5

Each task has a hard acceptance gate. Do not skip ahead to AI-generated reports.

## EPIC 0 — Repository and legal baseline
- [ ] T0001 Build supplied solution skeleton.
- [ ] T0002 Vendor/extract TE2 snapshot unchanged under `vendor/TabularEditor2`.
- [ ] T0003 Record TE2 license and third-party notices.
- [ ] T0004 Build TE2 unchanged if environment supports it; document baseline failures.
- [ ] T0005 Add current dependency/license inventory command/report.
- [ ] T0006 Mark DAX Studio source as Ms-RL external/reference only.

**Gate:** Core/CLI build, license boundaries documented, no mixed-license copying.

## EPIC 1 — Workspace / Connection Hub
- [ ] T0101 PBIP/PBIR/TMDL scanner.
- [ ] T0102 Git repo/status discovery.
- [ ] T0103 connection profiles: local PBIP, Desktop, Fabric, XMLA.
- [ ] T0104 runtime capability report.
- [ ] T0105 environment doctor for dotnet/git/node/npx/Power BI/DAX Studio.
- [ ] T0106 path-length/OneDrive/unappliedChanges warnings.

**Gate:** read-only local workspace appears in WPF and CLI.

## EPIC 2 — Fabric REST read-only control plane
- [ ] T0201 `IAccessTokenProvider` + fake test provider.
- [ ] T0202 Fabric REST client base with typed result/error model.
- [ ] T0203 List workspaces.
- [ ] T0204 List items per workspace.
- [ ] T0205 Get item metadata.
- [ ] T0206 Get item definition + decode parts.
- [ ] T0207 Get semantic model definition as TMDL.
- [ ] T0208 LRO poller.
- [ ] T0209 429 Retry-After handling.
- [ ] T0210 local cloud snapshot store.
- [ ] T0211 audit journal.

**Gate:** select Fabric semantic model -> download TMDL snapshot, no mutation.

## EPIC 3 — Power BI service read-only adapter
- [ ] T0301 raw REST client for Power BI service.
- [ ] T0302 list workspaces/reports/semantic models.
- [ ] T0303 data source/refresh status reads.
- [ ] T0304 add optional official `Microsoft.PowerBI.Api` adapter after pinning package.
- [ ] T0305 map Power BI/Fabric DTOs into common domain objects.

**Gate:** transport router can show same workspace/model estate from supported APIs without leaking provider DTOs.

## EPIC 4 — Semantic engine / TE2 modernization
- [ ] T0401 characterize TE2 object enumeration.
- [ ] T0402 modern semantic abstraction.
- [ ] T0403 BPA integration.
- [ ] T0404 dependency graph.
- [ ] T0405 measures/relationships/roles/perspectives/translations inventory.
- [ ] T0406 calculation groups/UDF awareness.
- [ ] T0407 TMDL snapshot reader.
- [ ] T0408 object-level semantic diff.

**Gate:** local and cloud TMDL both produce comparable semantic inventories and BPA results.

## EPIC 5 — XMLA/TOM live model
- [ ] T0501 XMLA connection adapter.
- [ ] T0502 Desktop local XMLA connect using External Tool args.
- [ ] T0503 remote Fabric/Power BI XMLA connect.
- [ ] T0504 DAX query execution.
- [ ] T0505 read-only DMVs/model metadata.
- [ ] T0506 capability detect read-only vs read/write.
- [ ] T0507 precise mutation transaction interface (disabled until Epic 7).

**Gate:** one selected model can be inspected through TMDL snapshot and live XMLA, with differences surfaced.

## EPIC 6 — DAX Studio integration
- [ ] T0601 configurable DAX Studio/dscmd discovery.
- [ ] T0602 launch DAX Studio with server/database/query file.
- [ ] T0603 dscmd query result export.
- [ ] T0604 dscmd benchmark.
- [ ] T0605 dscmd VPAX.
- [ ] T0606 error/cancel/process timeout handling.
- [ ] T0607 DAX Lab Light editor/result panel.

**Gate:** `Open advanced analysis` launches correct model/query; benchmark result can be attached to PbiBench QA record.

## EPIC 7 — Safe write/action transaction framework
- [ ] T0701 typed Action metadata/context/preconditions.
- [ ] T0702 finding -> plan -> preview diff.
- [ ] T0703 snapshot provider.
- [ ] T0704 approval levels.
- [ ] T0705 local rollback.
- [ ] T0706 remote mutation journal.
- [ ] T0707 first safe actions: formatting/descriptions/display folders.
- [ ] T0708 calendar/measure table/explicit measures.
- [ ] T0709 BPA autofix subset.

**Gate:** no action can write without plan + snapshot + approval + post-validation.

## EPIC 8 — Cloud definition write
- [ ] T0801 semantic model `updateDefinition` adapter.
- [ ] T0802 generic item `updateDefinition` adapter.
- [ ] T0803 `.platform` update policy.
- [ ] T0804 re-pull/verify after write.
- [ ] T0805 reject dirty/unversioned local target without explicit override.
- [ ] T0806 integration test in disposable workspace only.

**Gate:** controlled test semantic model round-trip TMDL update + validation + recovery.

## EPIC 9 — Git / PBIP engineering
- [ ] T0901 baseline snapshot/commit workflow.
- [ ] T0902 semantic object diff mapped to files.
- [ ] T0903 PBIR diff grouping.
- [ ] T0904 selective stage/restore.
- [ ] T0905 CI CLI validation.
- [ ] T0906 branch/deployment metadata.
- [ ] T0907 environment configuration/Variable Library mapping.

**Gate:** PR-ready validation report produced headlessly.

## EPIC 10 — PBIR report engineering
- [ ] T1001 clean-room PBIR model from public schemas.
- [ ] T1002 report/page/visual/bookmark tree.
- [ ] T1003 field binding validation against semantic model.
- [ ] T1004 visual calculation inventory/editor.
- [ ] T1005 visual interaction/layer/filter manager.
- [ ] T1006 Desktop Bridge reload.
- [ ] T1007 screenshots/regression.
- [ ] T1008 accessibility/report rules.

**Gate:** generate/modify a disposable report page, validate, reload, screenshot, diff.

## EPIC 11 — Fabric estate/admin
- [ ] T1101 admin workspace inventory.
- [ ] T1102 admin item inventory preview adapter.
- [ ] T1103 capacity association/inventory.
- [ ] T1104 permissions visualization.
- [ ] T1105 refresh/activity summary where APIs support it.
- [ ] T1106 FUAM deployment detection/link.
- [ ] T1107 FCA detection/link.
- [ ] T1108 admin preview badges and request quota handling.

**Gate:** useful estate dashboard remains read-only by default.

## EPIC 12 — Fabric lifecycle/deploy
- [ ] T1201 deployment plan abstraction.
- [ ] T1202 create/update selected supported Fabric items.
- [ ] T1203 deployment pipeline integration.
- [ ] T1204 Variable Library/environment mapping.
- [ ] T1205 service-principal automation mode.
- [ ] T1206 ARM capacity adapter (read-only first).
- [ ] T1207 capacity mutation behind highest approval level.

**Gate:** non-prod deployment is repeatable with no hardcoded environment IDs/secrets.

## EPIC 13 — Senior Playbook / SQLBI Radar
- [ ] T1301 rule metadata registry.
- [ ] T1302 current model relevance detection.
- [ ] T1303 safe vs benchmark-only labels.
- [ ] T1304 knowledge source URL/date/provenance.
- [ ] T1305 generate test idea from selected knowledge card.
- [ ] T1306 `IsAvailableInMDX`, implicit measure, filter behavior and DirectQuery benchmark cards.

**Gate:** every recommendation cites evidence and has a test/validation path.

## EPIC 14 — DataForge
- [ ] T1401 stable contract reader.
- [ ] T1402 truth manifest -> DAX assertions.
- [ ] T1403 source/gold/semantic lineage mapping.
- [ ] T1404 deterministic demo project generator integration.

**Gate:** known truth survives model/report automation regression.

## EPIC 15 — VizForge
- [ ] T1501 neutral VizSpec.
- [ ] T1502 WebView2/D3 preview.
- [ ] T1503 native PBIR visual mapping.
- [ ] T1504 custom pbiviz scaffold using official tools.
- [ ] T1505 themes/design tokens.
- [ ] T1506 accessibility/performance budgets.

**Gate:** one VizSpec renders in preview and exports either native PBIR or generated custom visual.

## EPIC 16 — AI / MCP
- [ ] T1601 MCP client host.
- [ ] T1602 Power BI Modeling MCP external adapter.
- [ ] T1603 Fabric Core MCP external adapter.
- [ ] T1604 PbiBench MCP server with high-level safe tools.
- [ ] T1605 map tool calls to approved plans.
- [ ] T1606 agent audit/replay.

**Gate:** Codex can inspect/propose; no unapproved mutation possible.

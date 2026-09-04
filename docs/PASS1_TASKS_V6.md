# Pass 1 Task List — deliver TE2++ inside PbiBench

## P0 — baseline
- [ ] P1001 Build current PbiBench scaffold.
- [ ] P1002 Build bundled TE2 upstream unchanged.
- [ ] P1003 If online, fetch/pin TE2 2.28.0 and repeat build.
- [ ] P1004 Record licenses/third-party notices.
- [ ] P1005 Record baseline tests/issues.

## P1 — integrated Model experience
- [ ] P1101 Add `PbiBench.ModelEditor` project/boundary.
- [ ] P1102 Host/adapt TE2 model tree.
- [ ] P1103 Host/adapt TE2 property editor.
- [ ] P1104 Preserve expression editing.
- [ ] P1105 Preserve create/edit/delete common semantic objects.
- [ ] P1106 Preserve selection/multi-selection.
- [ ] P1107 Preserve dependencies.
- [ ] P1108 Preserve undo/redo.
- [ ] P1109 Preserve scripting entry point.
- [ ] P1110 Preserve BPA.

**Gate:** user can perform normal TE2 semantic editing inside PbiBench Model area.

## P2 — PbiBench shell
- [ ] P1201 modern app shell/navigation.
- [ ] P1202 current model/project breadcrumb.
- [ ] P1203 connection status.
- [ ] P1204 Git branch/dirty indicator.
- [ ] P1205 findings/inspector pane.
- [ ] P1206 output/test log pane.
- [ ] P1207 keyboard command palette skeleton.
- [ ] P1208 original light editorial design tokens.

## P3 — Automation panel
- [ ] P1301 typed action contracts.
- [ ] P1302 preview object diff.
- [ ] P1303 apply/undo transaction.
- [ ] P1304 DAX format action.
- [ ] P1305 explicit SUM measure action.
- [ ] P1306 measure table action.
- [ ] P1307 SummarizeBy None action.
- [ ] P1308 display-folder action.
- [ ] P1309 description template action.
- [ ] P1310 Last Refresh scaffold action.
- [ ] P1311 BPA safe-fix adapter.

**Gate:** at least five actions preview; three apply successfully with undo.

## P4 — BPA+
- [ ] P1401 rule severity/category UI.
- [ ] P1402 object navigation.
- [ ] P1403 rationale/explanation panel.
- [ ] P1404 before/after preview.
- [ ] P1405 safe/unsafe fix classification.

## P5 — DAX bridge
- [ ] P1501 configure/discover DAX Studio path.
- [ ] P1502 save current expression/query to temporary `.dax`.
- [ ] P1503 launch DAX Studio with server/database/file.
- [ ] P1504 errors/process discovery UX.
- [ ] P1505 DAX formatter integration.
- [ ] P1506 dependency navigation from expression editor.

## P6 — model diagram
- [ ] P1601 graph DTO from semantic relationships.
- [ ] P1602 render table nodes.
- [ ] P1603 cardinality labels.
- [ ] P1604 active/inactive style.
- [ ] P1605 filter direction.
- [ ] P1606 click selects model object.
- [ ] P1607 basic auto-layout.

## P7 — PBIP/Git awareness
- [ ] P1701 detect active PBIP root if available.
- [ ] P1702 detect TMDL/PBIR.
- [ ] P1703 Git branch/status.
- [ ] P1704 changed semantic file count.
- [ ] P1705 path length warning.
- [ ] P1706 `unappliedChanges.json` warning.

## P8 — tests
- [ ] P1801 TE2 baseline characterization tests.
- [ ] P1802 action preview tests.
- [ ] P1803 action apply/undo tests.
- [ ] P1804 diagram DTO tests.
- [ ] P1805 DAX Studio command construction tests.
- [ ] P1806 PBIP/Git detector tests.
- [ ] P1807 smoke-launch test where possible.

## STOP GATE

Stop after this gate and show:
- screenshots/video if possible,
- build output,
- tests,
- known limitations,
- licenses,
- next Pass 2 recommendations.

Do not silently continue into full Fabric/PBIR/AI implementation.

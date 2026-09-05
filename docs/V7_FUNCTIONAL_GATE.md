# V7 functional and structural gate

Verification date: 2026-09-05. Starting repository commit: `7d8b439` (`v1.5`).

Status: **functional/structural gate COMPLETE**. All configured automated checks passed. The two visual-only checks below remain manual pending, as explicitly directed by the user, and do not block V9.1.

The user's 2026-09-05 instruction accepts the repository review as the V7 implementation review and explicitly separates the following visual-only checks from implementation acceptance:

- Windows taskbar icon appearance: **manual visual QA pending**.
- Pixel-level appearance at Windows 100%, 125%, and 150% scaling: **manual visual QA pending**.

No screen-control access is requested or used for this verification. These two checks do not block the functional/structural gate or sequential V9 work. The executable/window icon resource and original multi-resolution icon remain in the product and are covered by automated asset checks; this does not establish taskbar appearance.

## Verification command

`./scripts/build-pass1.ps1 -Configuration Release -Offline`

The command builds the pinned TE2 foundation and PbiBench, runs all configured product test projects and the available offline upstream regression suites, then launches the real WPF application with its isolated-profile smoke harness. Evidence: `artifacts/v7-functional-gate.log` and `artifacts/build/`.

The existing 371 V6 test executions and 15 original launch checks must remain covered. New V7 tests cover command routing, native editor operations, selection inspector projections, icon assets, and layout persistence.

## Corrections found by verification

- Correct the new native-command test's reflection of `FormMain.UI`: the pinned upstream API is a field, not a property.
- Correct focus-aware shell Undo/Redo: use the model undo manager when the hosted form lacks focus, preserving upstream text undo while the editor is focused. Regression coverage exercises native text undo independently from model undo and catches stale expression focus after a shell click.

## Results

PbiBench Release build: **0 warnings, 0 errors**. The pinned upstream TE2 build retains its existing reference-resolution warnings; its build, configured tests and launch checks passed.

| Suite | Passed | Failed |
|---|---:|---:|
| Adapters / contracts / layout / icon, net10.0 | 39 | 0 |
| Adapters / hosted TE2 commands, net48 | 41 | 0 |
| Semantic / Automation / inspector, net48 | 18 | 0 |
| Upstream TOM and remote-write boundary | 266 | 0 |
| Upstream scripts / parser / tree / formatter | 26 | 0 |
| **Total test executions** | **390** | **0** |
| Original application launch checks | **15** | **0** |

The 371 V6 executions remain covered, with 19 added V7 executions. No existing tests were removed or skipped. Immutable test/report copies are in `artifacts/v7-functional-evidence/`; launch evidence and generated page captures are in `artifacts/build/smoke-Release-3e0de4ffcd164c5398f1ab0ac50293d2/`. These captures are automated rendering evidence, not pixel-level Windows DPI or taskbar verification.

V9 may now proceed in the order specified by `contracts/V9/MILESTONES_V9.md`. The final V9 acceptance gate remains a separate requirement.

## Preserved boundaries

PbiBench remains the main C#/.NET application. TE2 2.28 remains hosted in-process with its full model editing, expression/property editors, script engine, and advanced command fallback. DAX Studio remains standalone. No framework retargeting or runtime dependency upgrade is performed for visual QA.

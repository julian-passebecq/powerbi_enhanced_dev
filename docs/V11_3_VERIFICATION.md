# V11.3 verification — 2026-09-05

Implemented locally from `4caccae9f4751555cbe584ffbf02e81e2fb88f77`. Product 11.3.0; Fabric Toolbox 0.2.0. Input ZIP SHA256 matched `32e14d6f6dbca5f6dcf22e1d8ed9beaaf4b339f6838cccd3e25534db7b096f0e`.

## Result

The final relevant Release gate passed, including **311 test executions, zero failures and zero skips**, fresh packaging, both process launch smokes and dependency isolation. Full solution build: zero warnings and zero errors.

```powershell
./scripts/invoke-v11-gate.ps1 -Configuration Release -Scope ModularGrowth
```

Evidence directory: `artifacts/v11-gate-ModularGrowth-Release-1404c429b69b4f77af8f8c1b8424d73c/`.

| Gate component | Result |
| --- | --- |
| V11 portable modules, net10.0 | 107 passed |
| V11 portable modules, net48 | 107 passed |
| Isolated Toolbox WPF | 3 passed |
| Relevant native adapters: Safe/Trusted, Fabric, ModelEditor | 47 passed |
| Semantic AI capture / script preview | 12 passed |
| WPF Feature Map, C#, recovery/export and Fabric workspace | 35 passed |
| Fresh portable package | Produced |
| Packaged Semantic IDE launch / native model smoke | Passed; 20 checks |
| Packaged Fabric Toolbox launch | Passed; five pages and offline V0.2 views |
| Toolbox packaged files / loaded-assembly isolation | Passed |
| Packaged detailed catalog versus embedded sources | Matched |
| Git whitespace check | Passed |

The gate directory contains `gate.log`, six TRX files, `gate-result.json`, `semantic-smoke/smoke-result.json`, `toolbox-smoke.txt`, screenshots and the tested `package/` directory. Test executions count the same portable tests once per runtime; this is the relevant subset, not the full historical suite.

## What was verified

- All twelve module records validate; feature rows resolve module IDs. Strict JSON, lifecycle, ownership, runtime metadata/compatibility, dependency cycles and transitive forbidden dependencies are tested. Actual project-reference closures are checked as well as catalog declarations. Embedded/disk/generated catalogs agree.
- Fabric tests verify fixed-origin GET requests, encoded continuation tokens, bounded pages/rows/responses, partial-result notices, cancellation, unsupported types, UTC times/duration, errors and failure details. Export tests verify an explicit metadata allowlist, absence of endpoint credentials, spreadsheet formula escaping and canceled-write preservation. Toolbox WPF tests verify filtering, identity details, manual job refresh, read-only rows and clearing stale workspace/item state.
- TE2 compiler diagnostics navigate to their originating document/position without execution and reject stale source. Macro context survives tab edits, disables incompatible loading with a reason and rejects incompatible preview/Trusted execution before snapshot creation. Legacy macro metadata still loads. Every new semantic snippet compiles through TE2; every Safe snippet produces a nonempty detached model preview without modifying the original model.
- Existing recovery/file-conflict tests, AI Context Export tests and relevant local undo/snapshot/Safe/Trusted/native model checks pass. Packaged smoke opens a synthetic BIM, checks native editing/Undo, BPA, DAX/diagram availability, safe preview, compiler Problems, AI export and Feature Map. No TE2 source or unrelated upstream package was changed.

## Visual inspection and development failures

The first gate stopped on `ScriptFileTests.OverwriteReviewIsBoundToObservedBytesAndDestination` on net48 because Windows refused `File.Replace`. The isolated rerun passed and both subsequent complete module runs passed. The file-write implementation and its fail-closed review checks were not changed. Evidence is retained in `artifacts/v11-gate-ModularGrowth-Release-dffe47cac7124c7f80d4f198a2869487/`.

The next gate passed all 311 tests, then the new C# screenshot smoke exposed an editor-height problem: Problems occupied space inside an already split editor pane. Problems now lives in the output pane beside the risk/output tab. The focused 28 C#/recovery/export WPF tests and a direct layout smoke passed, followed by the final gate above. The final smoke asserts a visible native editing area before capture. The unsuccessful gate remains in `artifacts/v11-gate-ModularGrowth-Release-c6c8191a9f6344e9918237c89c59eee0/`.

Inspected `v11-csharp-problems.png` and `v11-feature-map.png` from the corrected layout smoke, and `toolbox-smoke.png` from the final package: editor/Problems, module lifecycle details and Operations controls/results/details are readable. The final package also captures the corrected C# and Feature Map views.

## Launch the tested package

```powershell
& 'D:\PROJ\powerbi_enhanced_dev\artifacts\v11-gate-ModularGrowth-Release-1404c429b69b4f77af8f8c1b8424d73c\package\PbiBench.exe'
```

Keep the package dependencies together. Apps / Tools launches its separate Fabric Toolbox. Open Feature Map for module/lifecycle details; use Automate → Scripts / recorder / macros for C# Problems, context rules and semantic snippets. See [implementation notes](V11_3_IMPLEMENTATION.md) and [Fabric usage/API limits](FABRIC_TOOLBOX_V02.md).

No live Fabric/Power BI tenant or credentials were used. Recent authenticated job history remains a manual integration check. Hosted workflow definitions were updated to include offline Toolbox WPF tests; no push, hosted CI run or external review is claimed. Changes remain in the local working tree.

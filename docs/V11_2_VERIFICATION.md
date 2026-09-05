# V11.2 Feature Map verification — 2026-09-05

Baseline: `675a5026749094206339c312b7f190964953ce57`. Version: `11.2.0`. Commit label: `v11.2 - Feature Map and provenance UX`.

## Development checks

Executed the document generator in Release: `dotnet run --project scripts/FeatureCatalogGenerator -c Release -- <repository-root>`.

Executed focused `PbiBench.V11.Tests` with `FullyQualifiedName~FeatureCatalogTests|FullyQualifiedName~PlatformTests` in Release: 24 passed on net10.0 and 24 passed on net48. These cover schema/bounds, official evidence URLs, provenance joins, product focus, filters, embedded metadata and generated-document drift.

Executed focused `PbiBench.App.Tests` with `FullyQualifiedName~FeatureMapTests` in Release: 4 passed. An initial test fixture used an unshown WPF owner window and failed; the fixture was corrected to create its owner handle before exercising the same factory as Apps / Tools. The complete four-test subset then passed.

## Final relevant Release gate

Executed once after the targeted fixes:

```powershell
./scripts/invoke-v11-gate.ps1 -Configuration Release -Scope FeatureMap
```

Evidence: `artifacts/v11-gate-FeatureMap-Release-6e2ea30f7fad4ddca9b2b1ad3a02510e/`.

| Check | Result |
| --- | --- |
| Full solution Release build | Passed; 0 warnings, 0 errors |
| Catalog/provenance tests, net10.0 | 24 passed |
| Catalog/provenance tests, net48 | 24 passed |
| Focused WPF Feature Map tests | 4 passed |
| Fresh package under `package/` | Produced |
| Packaged Semantic IDE smoke | Passed |
| Packaged Fabric Toolbox smoke | Passed |
| Toolbox runtime isolation | Passed |
| Packaged detailed catalog versus embedded sources | Matched |

All 52 test executions passed with no skips. `gate.log`, three TRX files and `gate-result.json` record the gate. `semantic-smoke/smoke-result.json` includes the new map, filters, retained Provenance tab and document checks plus the preserved native TE2 model/BPA/Undo checks. `semantic-smoke/v11-feature-map.png` was visually inspected; the six columns, filters and selected-row details are readable. `toolbox-smoke.txt` records the separate five-page Toolbox process launch without loaded TE2/ModelEditor assemblies.

The final Git formatting check then removed one extra trailing blank line from generated Markdown by normalizing generator output. The dedicated embedded-catalog/provenance/document consistency test was rerun on net10.0 and net48: one passed on each. The gate package remains the successful pre-formatting snapshot; its document differs only by that trailing blank line.

## Boundaries

No live Power BI/Fabric target or credentials were used. The gate is the relevant subset, not the full historical test suite. Existing GitHub workflows discover the new portable tests; the V11.2 hosted run inspection and post-push audit are left to the separate reviewer.

Feature Map comparisons assess official public documentation; they are not hands-on parity tests or implementation commitments. Frozen and future areas remain unchanged. TE2 remains pinned at 2.28.0 with the existing patches, Semantic IDE stays net48, and Fabric Toolbox stays an isolated net10.0-windows process. No capability expansion or repository-settings change was made.

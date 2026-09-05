# Gen-2 Pass 1 verification — September 5, 2026

Baseline: `bbb29c3ab7adb2e7b9c04bf71b618354847e3e92`, confirmed as local and fetched `origin/main` before implementation. The full V2.1 pack is retained under `contracts/V2.1/`.

One final impacted Release gate ran using `scripts/invoke-v2-gate.ps1` and .NET SDK 10.0.400. It passed without retries. The complete solution built with **0 warnings / 0 errors**.

| Impacted suite | Runtime | Passed |
| --- | --- | ---: |
| Gen-2 PBIR, schema provenance, gallery and external arguments | .NET 10 | 25 |
| Catalog, module boundaries, platform and language | .NET 10 | 66 |
| Same portable contracts in the Semantic IDE runtime | .NET Framework 4.8 | 66 |
| DAX/C# UI, Feature Map, V11 workspaces and connected write guards | .NET Framework 4.8 | 38 |
| Diagram authoring, script previews, semantic automation and inspector | .NET Framework 4.8 | 41 |
| Safe interpreter, editor boundaries and DAX Studio adapter | .NET Framework 4.8 | 37 |
| DAX language service | .NET Framework 4.8 | 74 |
| **Total** | | **347** |

The packaged Semantic IDE launched and passed **51** checks: existing TE2 tree/property/Undo, relationship metadata, BPA, DAX workspace, Safe/Trusted separation, provenance, nine gallery recipe preview/apply/Undo round trips, report usage in the existing Semantic View, disabled offline Bravo, and Quick Open routing. Screenshots were captured and inspected, including the final DAX layout.

The packaged Report Studio launched independently and passed **10** checks covering PBIP discovery, tree/wireframe/inspector, offline schema validation, local measure lineage, and five actions through UI preview/apply: title, annotation, duplicate visual, duplicate page and field mapping. Each action restored the original definition byte hashes. Its screenshot was inspected. Additional unit cases cover cross-report copying, stale source and destination rejection, failed atomic replacement with rollback, backup tampering, unknown schema/properties, malformed metadata, duplicate JSON keys, path traversal, cancellation, alias handling and inventory export write boundaries.

Portable packaging passed and retains Microsoft schema licensing/source hashes plus transitive package notices. The Report Studio runtime folder contains no TE2, TOMWrapper, Semantic IDE, ModelEditor, Semantic or Fabric runtime assemblies. Source schema hashes and module project-reference boundaries passed their tests. `git diff --check` passed before commit.

Local evidence root: `artifacts/v2-gate-57c8b9a952de4d8f862ef54582e32691/`. It contains `gate-result.json`, `gate.log`, seven TRX files, `semantic-smoke/smoke-result.json`, `report-smoke.json`, PNG captures and the tested portable `package/`.

These are offline Windows checks using synthetic metadata. Live Bravo, Power BI Desktop rendering, DAX Studio query execution, XMLA and Fabric were not exercised. External argument construction is covered with replaceable process fakes. Post-push hosted results were subsequently confirmed successful for audited commit `1e02628f7b35af0e5b92c0452f86d3b102562cc2`: [fast run 33981051344](https://github.com/julian-passebecq/powerbi_enhanced_dev/actions/runs/33981051344) and [Windows Release run 33981051317](https://github.com/julian-passebecq/powerbi_enhanced_dev/actions/runs/33981051317), both started 2026-09-05 17:28 UTC. These V11 workflows did **not** run V2 tests or build/isolate Report Studio; their success is not hosted V2 evidence. Pass 2 adds that coverage.

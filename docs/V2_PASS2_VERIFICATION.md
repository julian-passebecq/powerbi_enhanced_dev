# V2 Pass 2 verification — September 5, 2026

Baseline: fetched `main`, `1e02628f7b35af0e5b92c0452f86d3b102562cc2`. All 11 files in the supplied ZIP were read and retained under `contracts/V2.2/`. The implementation and explicit supported/deferred boundaries are described in [V2_PASS2_IMPLEMENTATION.md](V2_PASS2_IMPLEMENTATION.md).

Targeted Debug tests covered the changed TMDL/catalog, PBIR actions/recovery, semantic event guards/Undo, Gallery draft compilation/insertion and offline Fabric snapshot transport/storage paths during development. No live tenant was accessed.

## Final impacted Release gate

`scripts/invoke-v2-gate.ps1` ran once with .NET SDK 10.0.400 and passed. The complete solution built with **0 warnings / 0 errors**. No Release test suite was rerun. During the gate review, the report-usage label was corrected to count distinct reports; the shell received one incremental Release rebuild, also with 0 warnings / 0 errors, before packaging and the packaged semantic smoke. The packaged executable matches the final build hash below.

| Impacted suite | Runtime | Passed |
| --- | --- | ---: |
| V2 PBIR, schema policy/provenance, TMDL/catalog, impact, Gallery and external arguments | .NET 10 | 57 |
| Complete V11 portable regressions, including Fabric snapshot transport/storage and catalogs | .NET 10 | 141 |
| Same complete V11 regressions in the Semantic IDE runtime | .NET Framework 4.8 | 141 |
| DAX/C# UI, Feature Map, V11 workspaces and connected write guards | .NET Framework 4.8 | 39 |
| Diagram authoring, script previews, semantic automation, inspector and semantic impact | .NET Framework 4.8 | 44 |
| Offline Fabric Toolbox WPF integration | .NET 10 Windows | 4 |
| Safe script/model-editor/DAX editor/process adapter boundaries | .NET Framework 4.8 | 37 |
| Complete DAX language service regressions | .NET Framework 4.8 | 74 |
| **Total** | | **537** |

All eight TRX files report zero failures and zero skipped tests.

## Packaged application evidence

- Semantic IDE: **52 checks passed**, including retained TE2 2.28.0 editing/BPA/Undo, nine Gallery recipe preview/apply/Undo round trips, dynamic-format incompatibility rejection, catalog consistency and report usage availability. The compatible dynamic-format apply/Undo case is covered in the semantic test suite.
- Report Studio: **14 checks passed**, including separate WPF launch, schema validation, cached lineage, synchronized navigation/search, zoom/fit and seven reviewed actions followed by exact original-byte restoration.
- Fabric Toolbox: the five-page WPF launch and loaded-assembly boundary smoke passed. Explicit report retrieval, 200/202/429, payload/path rejection, cancellation, new-directory publication and local handoff have offline fixture coverage in the suites above.
- Report Studio and Toolbox build outputs and packaged outputs passed process isolation. Report Studio has no TE2, Semantic IDE or Fabric authentication dependency.
- Packaging preserved TE2 and Microsoft schema licenses/attribution and included the Pass-2 implementation guide. Final Gallery and Report Studio captures were visually inspected.

Local evidence root: `artifacts/v2-gate-858481c2144e441ca1302ccc4b3b2453/`. It contains `gate-result.json`, `gate.log`, eight TRX files, `semantic-smoke/smoke-result.json`, `report-smoke.json`, `toolbox-smoke.txt`, PNG captures and the tested portable `package/`.

Final built and packaged `PbiBench.exe` SHA-256: `B38D8CD760A34C1E62FEDACDAA8B9387B63587DCD911A441123FDADC343069D0`.

## Scope and post-push validation

These are offline Windows checks using synthetic model/report metadata and replaceable HTTP/process transports. Live Power BI Desktop rendering, DAX Studio queries, Bravo, XMLA and authenticated Fabric report retrieval were not exercised. No remote report update capability is added. Formatting-copy support remains detector-only; bookmark mutations require the explicitly tested 1.0.0 contracts; unrecognized PBIR versions/schemas remain read-only.

The renamed hosted workflows now include V2 tests, Report Studio build/isolation and the offline Report Studio Release smoke. Their results for this commit are intentionally left to GitHub CI and the external reviewer after push. Historical successful V11-only runs are documented separately in [V2_PASS1_VERIFICATION.md](V2_PASS1_VERIFICATION.md); they are not V2 evidence.

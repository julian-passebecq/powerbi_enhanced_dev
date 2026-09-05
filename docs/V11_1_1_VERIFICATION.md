# V11.1.1 focused hardening verification — 2026-09-05

Implemented from baseline `47954aaf8876671eff879bbd0b809660f444e1fe` and published as commit [`675a5026749094206339c312b7f190964953ce57`](https://github.com/julian-passebecq/powerbi_enhanced_dev/commit/675a5026749094206339c312b7f190964953ce57). The local Release evidence below predates publication. The subsequent hosted results were confirmed on that exact commit during the V11.2 documentation update.

## Final Release gate

Executed `./scripts/invoke-v11-gate.ps1 -Configuration Release` successfully. Evidence is in `artifacts/v11-gate-Release-ec83f715b39d46b19a0858dcbf2b8864/`:

| Check | Result |
| --- | --- |
| Full `PbiBench.slnx` Release build | Passed; 0 warnings, 0 errors |
| `PbiBench.V11.Tests`, net10.0 | 70 passed |
| `PbiBench.V11.Tests`, net48 | 70 passed |
| Impacted net48 adapter tests | 47 passed |
| Impacted net48 semantic tests | 12 passed |
| Impacted net48 WPF tests | 28 passed |
| Fresh Release package | Produced under `package/` |
| Packaged Semantic IDE `--smoke-test … --v11` | Passed |
| Packaged Fabric Toolbox `--smoke-test …` | Passed |
| Toolbox output isolation | Passed |

The 227 test executions had no failures or skips. The adapter filter covers trusted scripting, safe scripts, Fabric transport/SQL and Model Editor boundaries. The semantic filter covers AI capture and script preview. The WPF filter covers V11 workspace and Fabric workspace tests. These are impacted suites, not the full historical suite.

`gate.log` records the run; the five TRX files record test results. `gate-result.json`, `semantic-smoke/smoke-result.json` and `toolbox-smoke.txt` record package/smoke outcomes. The package has its own configuration, pinned upstream provenance, licenses and SHA-256 inventory in `package/package-manifest.json`.

Focused development checks also passed: V11 tests on both targets and the 25-test V11 WPF subset. They were followed by the final gate above.

## Coverage and limits

Regression coverage includes legacy/current recovery detachment, advisory source preservation, external edits with identical timestamps/lengths, raw-byte encoding fingerprints, deletion, stale/path-mismatched overwrite reviews, Save As, cancellation, locks, storage bounds, invalid recovery schemas, programmatic/manual export option invalidation and cancellation during sampling. Completion tests exercise tables/columns/measures with spaces, quotes, backslashes, brackets, Unicode and partial escapes; the WPF integration test compiles inserted source through existing TE2 without executing it. Redaction tests cover counts, zero-count metadata, original-value exclusion and deterministic ZIP/checksum output.

The **V11 portable and Fabric fast gate** [run 33968940839](https://github.com/julian-passebecq/powerbi_enhanced_dev/actions/runs/33968940839) succeeded. The **V11 Windows Release module gate** [run 33968940844](https://github.com/julian-passebecq/powerbi_enhanced_dev/actions/runs/33968940844) also succeeded. Both runs used commit `675a5026749094206339c312b7f190964953ce57`.

The hosted Release logs show 70 net10 tests and 70 net48 tests passed, with zero failures/skips; Fabric Toolbox built with zero warnings/errors; the Toolbox output isolation check passed and TRX evidence was uploaded. These hosted results are separate from the 227 local test executions and packaged smokes above. Hosted coverage excludes main WPF/native TE2 execution and packaging; the local Release gate covers those boundaries.

Semantic IDE remains net48 with TE2 2.28.0 pinned at `75f10e331b8de0dda5c213180b9b8867b4a38191`, with the same two integration patches and licenses. Fabric Toolbox remains a separate net10.0-windows process. No Roslyn, debugger, embedded AI, PBIR, broad Fabric expansion or architectural migration was added.

Smokes used a synthetic BIM fixture. No live Power BI Desktop/Fabric target or credentials were used. Redaction is conservative and is not anonymization. Recovery intentionally requires explicit Save As/reopening instead of automatic rebinding. File saving compares hashes immediately before atomic replacement; it is not an OS-level compare-and-swap against an uncooperative concurrent writer. See `V11_IMPLEMENTATION.md` for behavior and limits.

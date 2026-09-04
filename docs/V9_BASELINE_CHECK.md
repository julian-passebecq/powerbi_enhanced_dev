# V9 prerequisite check — existing V6 baseline

Checked on 2026-09-04 against repository commit `1d0d3b1` (`v1`). The repository was clean before contract staging. Read `AGENTS.md`, `README_RUN.md`, and `docs/PASS1_DELIVERY_V6.md` before verification; no product code or build configuration was changed.

**The existing V6 automated baseline passes. V9 implementation remains blocked on completion of the separate pending UX pass, as confirmed by the user.** This run does not establish that the pending UX work is green. No V7/V8 contract or corresponding completed UX delivery was present in the existing workspace when it was inspected; only the V6 baseline and earlier UX design documents were available. Do not substitute V6 test success for the user's UX prerequisite.

## Verification

Ran `./scripts/build-pass1.ps1 -Configuration Release` with the existing .NET SDK 10.0.400 and Visual Studio 2022 toolchain. Tests, upstream tests, and launch smoke were all enabled. The command exited successfully with code 0.

| Check | Result |
|---|---:|
| PbiBench Release solution build | Passed; 0 warnings, 0 errors |
| Adapter tests, .NET 10 | 31 passed |
| Adapter and hosted-editor tests, .NET Framework 4.8 | 32 passed |
| Semantic and Automation tests | 16 passed |
| Upstream TOM property/undo and remote-review tests | 266 passed |
| Upstream script/parser/tree-path/formatter subset | 26 passed |
| Total automated test executions | **371 passed, 0 failed** |
| Isolated application launch smoke | **15 checks passed** |

The TE2 source remains pinned to `75f10e331b8de0dda5c213180b9b8867b4a38191` (2.28.0), with the existing integration patch preserved. Upstream assembly-reference warnings remain for Microsoft.Identity.Client.NativeInterop and Antlr4.Runtime; these did not fail the build or covered tests. They must not be represented as a warning-free upstream build or verified live authentication.

The smoke run populated the actual TE2 tree, selected a measure expression, edited a property with undo/redo, previewed/applied/undid automation, displayed BPA findings, followed diagram selection, prepared DAX, and saved a local model. Its `model.png` was visually inspected and shows the existing integrated model tree, expression editor, property grid, and shell. Six captures were generated. Smoke profiles and the model fixture were isolated; no interactive user process was closed.

## Evidence

- Complete command output: `artifacts/v9-baseline-verification.log`.
- Preserved TRX reports and upstream build log: `artifacts/v9-baseline-evidence/`.
- Fresh launch result: `artifacts/build/smoke-Release-ea82bff3cafc43b9913f612ca7dcb00a/smoke-result.json`.
- Fresh captures in the same smoke directory: `model.png`, `model-compact.png`, `automation.png`, `bpa.png`, `dax.png`, and `diagram.png`.

Live Desktop/XMLA/Fabric connection/save roundtrips, physical 150% DPI testing, the separate pending UX pass, and V9 features were not validated by this run. Resume the sequential V9 milestones only after the user-identified UX pass completes and its own verification is green.

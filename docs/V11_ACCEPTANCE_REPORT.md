# V11.1 impacted acceptance gate

Validated on Windows, 2026-09-05, starting from main `53813388fbf2a3d7075572fd0a33be207faeccdf`.

The complete solution builds with zero warnings/errors. The final impacted test results are:

| Target | Tests passed |
|---|---:|
| V11 export / C# / handoff / provenance, net10 | 38 |
| Same portable contracts on net48 | 38 |
| Impacted adapter tests: native editor, trusted scripting, safe parser, Fabric transport/SQL | 47 |
| Native semantic capture and detached script preview | 12 |
| New WPF workspace tests and existing Fabric view tests | 6 |
| Total framework-specific test runs | 141 |

`scripts/invoke-v11-gate.ps1 -Configuration Debug` passed solution build, the impacted suites, fresh portable packaging, and both packaged application launch smokes. Final completion-prefix/risk-scan adjustments were followed by another changed-project build, both portable frameworks, targeted WPF tests and the latest Semantic IDE smoke. The package layout/dependency lane was unchanged by those editor-only adjustments.

The Semantic IDE smoke verified 13 boundaries: native model/tree/selection/Undo, diagram, internal DAX workspace, BPA, detached script preview without mutation, compile-only positioned diagnostics, recipe-to-C# text, metadata-only export review, unchanged model after ZIP generation, bundled provenance, Toolbox discovery and Apps / Tools visibility. Export and C# editor screenshots were visually inspected. The Toolbox smoke launched all five shell pages and verified that TE2/ModelEditor assemblies were not loaded.

Local evidence is under ignored `artifacts/v11-gate-cec16582c0c34d98a34b2d6a2d960ca9` and `artifacts/v11-final-editor-smoke`. These paths contain synthetic fixture outputs and are not product dependencies.

No live PBIX, Power BI Desktop local engine, or authenticated Fabric integration was validated. SQL/sample query tests use fakes; the native editor smoke uses the supplied synthetic BIM. Runtime and functional limitations, privacy omissions, dependencies and CI exclusions are documented in `V11_IMPLEMENTATION.md`. The full historical V9 suite was not repeatedly rerun. No post-push self-audit is required; the GitHub diff is ready for the user's separate reviewer.

# V2 Pass 3 verification — September 5, 2026

Baseline: current `main` was fetched and matched audited SHA `70493e1a63064a7e6d2ec98c285d187a556834a3`. All 13 supplied ZIP files were read and retained under `contracts/V2.3/`. Scope and supported limitations are recorded in [V2_PASS3_IMPLEMENTATION.md](V2_PASS3_IMPLEMENTATION.md).

## Final impacted Release gate

`scripts/invoke-v2-gate.ps1` ran once with .NET SDK 10.0.400 and passed. The complete solution built with **0 warnings and 0 errors**. All **628 framework-specific tests passed**, with **0 failures and 0 skipped** across 13 TRX files. No Release suite or full gate was rerun. Targeted Debug tests and offline UI checks were used during implementation. No runtime code changed after the final gate.

| Suite | Runtime | Passed |
| --- | --- | ---: |
| V2 PBIR, schemas, semantic catalogs, impact, Gallery and companion arguments | .NET 10 | 57 |
| ExternalTools arguments, discovery, manifests and handoffs | .NET 10 / net48 | 12 + 12 |
| Design Exchange context, dashboard/theme validation and provenance | .NET 10 / net48 | 31 + 31 |
| Report Studio design-preview WPF | .NET 10 Windows | 1 |
| Complete V11 regressions, including exact module graph and Fabric snapshots | .NET 10 / net48 | 142 + 142 |
| Relevant DAX/C# UI, Feature Map, V11 workspaces, write guards and unified shell | net48 | 41 |
| Semantic diagram/script preview/automation/inspector/impact | net48 | 44 |
| Fabric Toolbox WPF integration | .NET 10 Windows | 4 |
| Script/editor/DAX/process boundaries | net48 | 37 |
| DAX language service | net48 | 74 |
| **Total** | | **628** |

## Packaged validation

- Semantic IDE: **56 smoke checks** passed. New checks cover the compact rail, Home/project strip, metadata/spec/theme exchange and component manifest. Existing TE2 editing, BPA, Undo, Gallery and report usage checks remain.
- Report Studio: **15 smoke checks** passed, retaining all seven preview/apply/exact-byte restore action round trips. Design Preview independently validates the handoff, renders proposed visuals and leaves every original PBIR file byte unchanged.
- Fabric Toolbox: five-page offline WPF smoke and loaded-assembly isolation passed; its report snapshot regression suites remain green.
- Both build and packaged child outputs pass isolation. Neither ships DaxStudio. Report Studio has no TE2/Fabric-auth dependency; Toolbox has no semantic UI/TOM dependency.
- `test-components.ps1` confirms the three declared executable paths, actual managed binary versions and retained original-icon/Microsoft-theme attribution.
- Final packaged Home, Design Preview and Fabric Toolbox screenshots were visually inspected. The native Report Studio editing view and Design Exchange input/diagnostic view were also inspected during targeted checks.

Evidence: `artifacts/v2-gate-1b6337b57a4d4c6781e050e05b72befe/` (`gate.log`, `gate-result.json`, TRX files, smoke results and screenshots).
Portable package: `artifacts/v2-gate-1b6337b57a4d4c6781e050e05b72befe/package/`; launch `PbiBench.exe`.

## Packaged runtime SHA-256

- `PbiBench.exe`: `d5d0d89b44bbc852793db7f1a9b7fc9619d07dd5b3753ac96ebdfd1aa74a8fbe`
- `report-studio/PbiBench.ReportStudio.dll`: `dcc791e8eeb4fc522ae02559d0a2edb8bf3f629b5ed144d8e2105eed8dfeb2ab`
- `fabric-toolbox/PbiBench.FabricToolbox.dll`: `35855c525ce4b1c91f95176795342c969242ca106134e1ae49066a9db3554e31`
- `PbiBench.ExternalTools.dll`: `72d785de9efc0fbbbf0ea6a9f28b61e3ee37e7baee79015edb44db248420c225`
- `PbiBench.DesignExchange.dll`: `3677e045abe3e28473bd2696f4ac972fb51ba8d7e5e5654fa9703f05df0798d3`

## Remaining external verification

No authenticated live Fabric/Power BI/XMLA, Desktop rendering, DAX Studio query or Bravo execution was performed. Contract/process handoffs use offline fixtures. No provider/network invocation was added to Design Exchange, and no remote PBIR write exists in the new route. Optional sampling stays in the existing reviewed Context Export utility. Design Preview is approximate design intent, not a pixel-perfect report renderer or PBIR generator.

Post-push hosted CI and architecture audit are intentionally left to the external reviewer, as requested.

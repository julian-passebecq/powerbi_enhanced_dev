# Feature provenance ledger - audited baseline

Baseline: `53813388fbf2a3d7075572fd0a33be207faeccdf`.

This ledger distinguishes **where an idea/problem is known from** from **where the implementation comes from**. Public TE3 documentation may be used as a capability benchmark, but proprietary TE3 code/assets/internals are not an implementation source.

| Feature / subsystem | Current implementation owner | Origin / dependency | Provenance class | Update boundary |
|---|---|---|---|---|
| Semantic model tree/properties/native model editor | `PbiBench.ModelEditor` + hosted TE2 | Tabular Editor 2.28 open-source source tree, pinned in `vendor/TabularEditor2-2.28.0` | TE2 MIT foundation | Update TE2 pin + patches + native regression suite |
| TOM wrapper/undo foundation | TE2/TOMWrapper | Tabular Editor 2 open source | TE2 MIT foundation | Same TE2 lane |
| Function Undo ordering correction | `vendor/patches/te2-2.28.0-function-undo-order.patch` | PbiBench local patch to TE2 MIT code | PbiBench patch on TE2 | Rebase/verify on TE2 update |
| Remote-write review correction | `vendor/patches/te2-2.28.0-remote-write-review.patch` | PbiBench local patch to TE2 MIT code | PbiBench patch on TE2 | Rebase/verify on TE2 update |
| Trusted C# scripting | `PbiBench.ModelEditor/TrustedScriptRunner` + TE2 ScriptEngine | TE2 open-source scripting engine | TE2-backed compatibility | Keep isolated as trusted mode |
| Safe C# Preview | `PbiBench.Core.Automation`, `PbiBench.Semantic.ModelAuthoring` | Original allowlisted C#-shaped parser + detached public TOM model | PbiBench original | Independent from TE2 UI; protected by safe-script tests |
| Action Recorder / Macro Library | PbiBench semantic/automation | Original typed recipe model | PbiBench original | PbiBench-owned |
| DAX language service | `PbiBench.Dax.LanguageService` | Original service; built-in identifiers sourced from TE2 grammar with license preserved; behavior based on public DAX syntax | Mixed: PbiBench original + TE2 MIT data + Microsoft public docs | Refresh grammar/function data separately from UI |
| DAX Query workspace | PbiBench App/Core/Semantic | Original UI/services over Microsoft Analysis Services/TOM query APIs | PbiBench original + Microsoft public API | Semantic IDE |
| DAX Studio handoff | `PbiBench.DaxStudio` | DAX Studio external application | External tool bridge | Detect/version/launch only; no vendored DAX Studio internals |
| DAX Scripts | `PbiBench.Semantic.ModelAuthoring` | Original PbiBench versioned text format and authoring service | PbiBench original | Semantic IDE |
| UDF Workbench | PbiBench semantic authoring | Microsoft public DAX FUNCTION/TOM metadata behavior | PbiBench original + Microsoft public API/docs | Semantic IDE |
| Calendar Editor | PbiBench semantic authoring | Microsoft public TOM Calendar/time-intelligence APIs | PbiBench original + Microsoft public API/docs | Semantic IDE |
| Perspective/Translation editors | PbiBench semantic authoring + TE2 setters | Existing TOM/TE2 metadata access; new PbiBench UI/workflow | PbiBench UI on TE2/TOM | Semantic IDE |
| Model Diagram / relationship authoring | PbiBench App/Semantic | Original PbiBench UI/workflow over semantic metadata | PbiBench original | Semantic IDE |
| Data Preview | `PbiBench.Core.DataExploration` + App | Original DAX/query generation over connected engine | PbiBench original + Microsoft DAX/TOM | Semantic IDE |
| Data Profiling | `PbiBench.Core.DataExploration` | Original generated DAX; public Microsoft DAX semantics used as reference | PbiBench original | Semantic IDE |
| Pivot Lab | PbiBench Data Exploration | Original generated DAX/layout workflow | PbiBench original | Semantic IDE |
| BPA native TE2 | hosted TE2 | TE2 BPA engine | TE2 MIT foundation | TE2 lane |
| PbiBench BPA packs | `PbiBench.Automation` | Independently authored rules using public Power BI/DAX guidance | PbiBench original | Rule-pack versioning |
| VertiPaq/VPAX workspace | PbiBench Core/Semantic/App | Original import/capture/optimization layer; public DMV interfaces; test VPAX fixture carries SQLBI attribution/license | PbiBench original + Microsoft public API + licensed fixture | Keep fixture provenance; no proprietary copying |
| Semantic tests/assertions | PbiBench Core/App | Original PbiBench test contracts/services | PbiBench original | Semantic IDE/CLI |
| PBIP/TMDL workspace | PbiBench Workspace/Semantic/Git | Microsoft public PBIP/TMDL formats/APIs + original comparison/sync logic | PbiBench original + Microsoft public formats | Workspace lane |
| Disk/Live/Git three-way sync | PbiBench Workspace/Semantic/Git | Original PbiBench implementation | PbiBench original | Workspace lane |
| Fabric API/auth/SQL services | `PbiBench.Fabric` | Microsoft Fabric/OneLake public APIs, MSAL, Microsoft.Data.SqlClient | PbiBench adapters + Microsoft/public packages | Fabric lane, independent release cadence |
| Fabric current UI | `PbiBench.App` | PbiBench UI over `PbiBench.Fabric` | PbiBench original | Migrate broad platform UI to Fabric Toolbox |
| Refresh/deployment | PbiBench Core/Semantic | Microsoft public XMLA/TMSL/TOM APIs + PbiBench review guards | PbiBench original + Microsoft APIs | Semantic deployment lane |
| CLI | `PbiBench.Cli` | Original PbiBench command schema/services | PbiBench original | Shared Core/Semantic contracts |
| Agent proposal schema | PbiBench Core/Automation | Original strict proposal/preview system | PbiBench original | Retain as local interchange if useful |
| Online OpenAI provider | PbiBench Agent provider | OpenAI public Responses API | Optional external service adapter | De-emphasize/hide; not core product |
| Semantic compiler prototype | PbiBench Core/Semantic | Original bounded Metric View YAML prototype using Databricks public documentation | PbiBench original + public format docs | Prototype lane; do not expand in V11 |
| Local DAX package prototype | PbiBench Core/Semantic | Original PbiBench manifest/lock format | PbiBench original | Prototype lane; do not expand in V11 |
| DataForge | `PbiBench.DataForge` / separate project concept | PbiBench-owned data-generation/truth workflow | PbiBench original | Separate app/contract lane |

## Required machine-readable equivalent

Codex should create `docs/architecture/provenance.json` with at least:

```json
{
  "schemaVersion": 1,
  "baselineCommit": "53813388fbf2a3d7075572fd0a33be207faeccdf",
  "components": [
    {
      "id": "semantic.model-editor.te2",
      "ownerProject": "PbiBench.ModelEditor",
      "sourceType": "te2-mit",
      "upstream": "TabularEditor/TabularEditor",
      "pin": "2.28.0 / repository pinned commit",
      "localPatches": [
        "vendor/patches/te2-2.28.0-remote-write-review.patch",
        "vendor/patches/te2-2.28.0-function-undo-order.patch"
      ],
      "updateLane": "te2"
    }
  ]
}
```

Do not invent a license field if it has not been verified; use `unknown-needs-review` instead.


## V11.1 additions to track

| Feature / subsystem | Intended owner | Origin / dependency | Provenance class | Update boundary |
|---|---|---|---|---|
| C# editor/language assistance | `PbiBench.CSharp.LanguageService` + `PbiBench.App` adapter | PbiBench-owned integration; optional Roslyn/Microsoft.CodeAnalysis only if a compatible version is adopted | PbiBench original + optional Microsoft OSS/package | C# language-service lane; must not leak into TE2 host contracts |
| AI Context Export | Core/export service + Semantic IDE UI | PbiBench original over existing semantic/query metadata services | PbiBench original | AI interchange/export schema lane |
| Fabric Toolbox executable | `PbiBench.FabricToolbox` | PbiBench UI over shared `PbiBench.Fabric` Microsoft API adapters | PbiBench original + Microsoft public APIs/packages | Fabric application lane independent from TE2 |

If Roslyn/Microsoft.CodeAnalysis is actually introduced, record the exact packages/versions/license and net48 compatibility in `provenance.json` and `DEPENDENCY_UPDATE_MATRIX.md`. If it is not introduced, do not list it as a dependency merely because this specification mentions it.

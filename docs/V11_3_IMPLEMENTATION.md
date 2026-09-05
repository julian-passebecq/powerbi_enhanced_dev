# PbiBench V11.3 — modular growth

Baseline: `4caccae9f4751555cbe584ffbf02e81e2fb88f77`. Product: **11.3.0**. Fabric Toolbox: **0.2.0**.

The supplied V11.3 ZIP matches SHA256 `32e14d6f6dbca5f6dcf22e1d8ed9beaaf4b339f6838cccd3e25534db7b096f0e`. Its documents are archived under `contracts/V11.3/` as reference material. The user's request governs implementation; embedded commit/push and reviewer directions do not themselves authorize publishing.

## Module ownership and lifecycle

`docs/architecture/module_catalog.json` records all twelve requested modules, versions, process kinds, target frameworks, owners, upstream dependencies, contract versions, update lanes, module dependencies, forbidden dependencies and protecting tests. Versions describe capability revisions, independently of assembly versions in shared product projects. Future report tooling has explicit planned ownership/runtime and version 0.0.0; it claims no implementation.

`Core.Platform.ModuleCatalog` validates bounded, strict JSON, duplicate/unknown fields, enum vocabularies, metadata, references, cycles, runtime compatibility and transitive forbidden dependencies. Tests also traverse actual project-reference closures and check references against the forbidden lists. The catalog cannot load or execute modules.

Feature catalog schema 2 replaces `focus` with `lifecycle` and adds required `moduleIds`. Active, Selective, Independent, Incubating, On demand and Later all allow future development. Maturity remains separate. Labs shows Labs and Future rows; companion integrations remain in Companions. Feature Map details now show module/version/process/runtime/update lane alongside existing provenance. TE3 comparison evidence and its public-document-only interpretation remain unchanged. Historical verification reports retain their original context; they do not define current lifecycle policy.

Regenerate the joined document with `dotnet run --project scripts/FeatureCatalogGenerator -c Release -- .`. Both catalogs and provenance are embedded, compared with their on-disk sources in tests, and packaged with the generated document.

## Fabric Toolbox V0.2

See [Fabric Toolbox V0.2](FABRIC_TOOLBOX_V02.md) for usage, API sources and limits. The five-page separate process now has item name/type filters, an identity inspector, stable-ID copy, bounded JSON/CSV filtered inventory export and a real manually refreshed Operations page. Results include item linkage, job type/status, UTC start/end, duration, public failure summary and instance/correlation IDs. Unsupported item types are explicit.

TE2 remains pinned at 2.28.0. No TE2 source, MSAL, SqlClient, native framework or unrelated upstream was upgraded. The Toolbox references Fabric/Core and contains no Semantic IDE, ModelEditor, Semantic, TOMWrapper or TabularEditor runtime.

## C# automation

**Automate → Trusted Legacy → Compile / review risks** now populates a read-only Problems grid with script, severity, code, line, column and message from the authoritative TE2 compiler. Selecting a row activates the originating script and moves the caret without running code. Diagnostics retain their source snapshot; edited or closed sources require a new compile. Closing/restoring documents releases old diagnostics. Existing risk review and explicit trust acknowledgment remain separate from compilation.

The macro library accepts optional bounded `Context` metadata in existing version 1 files:

```json
{
  "AllowedSelectionKinds": ["Measure"],
  "MinSelectedCount": 1,
  "MaxSelectedCount": 10,
  "RequiresConnectedModel": false
}
```

Allowed kinds are Model, Table, Column and Measure; empty means any kind. Counts are 0–10,000. Existing SafeScript / Recipe / TrustedLegacy mode values are unchanged, and old files without context still load. Unknown JSON fields and executable enable expressions are rejected. The library shows Enabled and Reason and disables incompatible loading. Rules stay attached to the loaded script tab through edits and are checked again at preview, apply entry and immediately before Trusted execution. Recovery restores detached source drafts and no trust; macro metadata remains in the macro library, and loading a macro reapplies its rules.

**Generate from selection** captures exact semantic object names into a new reviewable script tab. It supports numeric SUM measures, hiding selected key columns, measure folders, descriptions, format strings, selected DAX formatting and COUNTROWS measures. Non-numeric columns are explicitly skipped for SUM. All new snippets use Safe Preview except the existing TE2 `FormatDax()` helper, which generates Trusted text. There is no automatic preview/apply/Trusted execution. Up to 200 selected objects and 256 KiB generated text are allowed; names are escaped for C# and DAX. Safe and Trusted execution engines, local preview/undo and snapshot boundaries remain intact.

## Relevant Release gate

```powershell
./scripts/invoke-v11-gate.ps1 -Configuration Release -Scope ModularGrowth
```

This builds the solution, runs V11 modules on net10/net48 (including file conflict/recovery and AI export), isolated Toolbox WPF tests, relevant native Safe/Trusted/Fabric tests, focused semantic and WPF tests, then creates a fresh package and launches both packaged processes. Toolbox output and loaded assemblies are checked for forbidden dependencies. Hosted workflows now include offline Toolbox WPF tests; hosted run inspection is separate from local verification. See `V11_3_VERIFICATION.md` for actual results.

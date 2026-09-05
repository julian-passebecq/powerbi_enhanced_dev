# MASTER PROMPT V11.1 - Compartmentalization + AI Export + practical C# automation

Baseline audited before this pack:
- repository: `julian-passebecq/powerbi_enhanced_dev`
- main baseline: `53813388fbf2a3d7075572fd0a33be207faeccdf`
- repository evidence states V9.1-V9.6 automated functional gates are complete.

This pack **supersedes the earlier V10 and V11 planning packs**. Preserve working V9 behavior. Do not replay already implemented milestones.

## Product direction

PbiBench is a family of compartmentalized tools with one consistent launcher/switcher experience:

- **Semantic IDE / TE2++** - current `PbiBench.exe`; model, DAX, data exploration, authoring, automation, QA, optimization, PBIP/TMDL/Git and semantic-model-specific Fabric workflows.
- **Fabric Toolbox** - separate modern-.NET process for broad Fabric platform inventory/engineering/admin/monitoring workflows, reusing shared `PbiBench.Fabric` services.
- **DataForge** - separate data-generation/truth application using versioned contracts.
- **External tools** - DAX Studio, Power BI Desktop, VS Code/Codex via explicit launch/handoff.
- **AI interchange** - export context to any external AI and optionally import strict typed proposals. Do not make an embedded AI provider the core workflow.

Permanent rule: TE2 is the MIT semantic foundation/compatibility engine for the Semantic IDE, not the architectural owner of Fabric, DataForge, AI interchange or generic platform capabilities.

## Priority A - provenance and compartmentalization

Create/maintain:
- `docs/architecture/FEATURE_PROVENANCE_LEDGER.md`
- `docs/architecture/DEPENDENCY_UPDATE_MATRIX.md`
- `docs/architecture/provenance.json`

Every important feature must identify owner module/sub-app, provenance class, upstream/pin/version, local adapter, local patches, license/terms classification when applicable, update lane and protecting tests.

Add an Apps/Tools switcher that clearly distinguishes Semantic IDE, Fabric Toolbox, DataForge, DAX Studio, Power BI Desktop, optional VS Code/Codex, AI Context Export, and Provenance/About. Missing tools fail gracefully.

## Priority B - Fabric Toolbox boundary

Create a separate `PbiBench.FabricToolbox` executable using the existing `PbiBench.Fabric` service layer. Prefer modern .NET. It must not reference TE2/ModelEditor UI assemblies.

Keep semantic-model-specific Fabric functions in PbiBench. Broad workspace/OneLake/lakehouse/warehouse/pipeline/capacity/admin discovery belongs to Fabric Toolbox. Do not duplicate API clients. Do not destructively remove existing PbiBench Fabric screens until the new process boundary is proven.

## Priority C - AI Context Export

Implement privacy-reviewed export to a normal ZIP for use with any external AI/chat.

Support:
- entire model or selected tables/measures/objects;
- configurable per-table sample row counts;
- per-table and per-column include/exclude;
- samples OFF by default;
- metadata/DAX/relationships/calculation groups/UDFs/descriptions/dependencies;
- optional BPA/VertiPaq/tests/workspace context;
- optional automation reference for generating C# scripts;
- deterministic manifest and SHA-256 checksums;
- size estimate and hard bounds;
- cancellation;
- explicit sensitive-data review.

Never export credentials, tokens, connection strings or secret-bearing source properties. Do not claim sample rows are anonymized unless an explicit transformation was applied.

Preserve the existing strict local proposal import/preview architecture if useful, but no imported file can self-approve. Existing online Agent/provider code may remain optional/experimental; do not expand it in this pass.

## Priority D - practical C# improvements, not a Visual Studio clone

Do **not** abandon C#. Implement the reasonable high-value automation experience described in `CSHARP_AUTOMATION_IDE_SCOPE.md`:

- coherent multi-tab C# script workspace;
- syntax/structure editing basics;
- model/selection-aware completion;
- signature help;
- compile diagnostics before trusted run;
- small useful semantic snippets;
- advisory high-risk API hints;
- recorder -> typed Recipe + generated C# view;
- lightweight Macro Library search/tags/favorites;
- preserve Safe Preview and Trusted C# as distinct execution lanes.

Prefer an isolated `PbiBench.CSharp.LanguageService` with no WPF dependency and no process-global TE2 state where practical. Prefer Roslyn only if it integrates cleanly with net48 and existing TE2 runtime dependencies. Do not destabilize the host just to claim Roslyn. If necessary, keep the interface and use existing compiler/reflection-backed assistance now.

Explicit non-goals: C# debugger, project system, NuGet IDE, MSBuild UI, profiler, generic refactoring suite, Visual Studio parity.

## DAX decision

Keep PbiBench DAX IDE/query workspace. It is core semantic authoring. Keep DAX Studio external for deep Server Timings/query plans/specialist analysis and provide a visible handoff. Do not remove PbiBench DAX Query just because an external tool exists.

## Testing/token-efficiency contract

Follow `TESTING_DELEGATION.md`.

During implementation:
- build changed projects;
- run focused tests for changed modules;
- run native TE2/net48 tests only when native/model/undo boundaries changed;
- run one WPF/WinForms smoke when desktop UI changed;
- run packaging smoke only when runtime/package layout changed;
- run one final impacted gate before push.

Do **not** rerun the full historic 1,000+ suite after every edit. Do **not** spend a large reasoning pass writing a post-push self-audit.

Where possible, keep new language/export/provenance code pure and dual-targeted so a post-push reviewer can execute core/net10 tests independently. After push, external review will inspect the GitHub diff, architecture, security/provenance and test coverage and may run portable tests where the environment permits. Never delegate Windows/net48/WPF/native TE2 verification that only the Windows environment can perform.

If feasible without destabilizing the repo, add a Windows GitHub Actions fast gate so repetitive regression execution is paid by CI rather than LLM reasoning. Targeted local tests still run before push.

## Stop condition

Stop at `ACCEPTANCE_GATE_V11_1.md`. Do not start full DAX debugger, PBIR/report engineering, broad semantic compiler/package expansion or large new Fabric-admin feature sets in the same pass.

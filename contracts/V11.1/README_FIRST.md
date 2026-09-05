# PbiBench V11.1 - Compartmentalized Platform + AI Context Export + practical C# automation

Baseline used to prepare this delta pack:
- repository: `julian-passebecq/powerbi_enhanced_dev`
- main commit: `53813388fbf2a3d7075572fd0a33be207faeccdf`
- commit message: `v9.3 — Model Authoring Pro`
- repository evidence says V9.1-V9.6 functional/automated gates are complete.

This is a **delta pack**, not a source snapshot. It supersedes the earlier V10 and V11 planning packs. Do not replay already-implemented V9 functionality.

## Product decision

Keep one recognizable PbiBench family but compartmentalize ownership and update risk:

1. **Semantic IDE / TE2++** - current `PbiBench.exe`: model, DAX, data exploration, semantic automation/QA, PBIP/TMDL/Git, optimization and semantic-model-specific Fabric integration.
2. **Fabric Toolbox** - separate modern-.NET executable for broad Fabric platform inventory/engineering/admin/monitoring, reusing shared `PbiBench.Fabric` services.
3. **DataForge** - separate deterministic data/truth application connected by versioned contracts.
4. **External tools** - DAX Studio, Power BI Desktop, VS Code/Codex through explicit launch/handoff.
5. **AI Context Export** - export a privacy-reviewed context ZIP for any external AI; no embedded provider is required for the core workflow.

## C# decision

Do **not** abandon C# improvements, but do not build Visual Studio.

Add practical automation ergonomics:
- C# script workspace/tabs;
- syntax/structure editing basics;
- `Model` / `Selected` / semantic-object completion;
- signature help;
- compile diagnostics;
- useful semantic snippets;
- advisory risk hints for Trusted C#;
- recorder -> Recipe + readable generated C#;
- Macro Library search/tags/favorites where low-risk.

Preserve the current split:
- Safe Preview = restricted, detached, diffable, undoable;
- Trusted C# = full TE2 C#, explicit trust, not a sandbox.

The AI Context Export should optionally include the automation API/capability reference so any external AI can generate better C# without maintaining an embedded chat client.

## Testing decision

Codex owns Windows-only implementation verification. It should use focused tests during development and one final impacted gate, not repeatedly burn cycles on the full historic suite or a long self-audit.

Post-push ChatGPT review will inspect the GitHub diff, architecture, provenance, test coverage, security boundaries and CI, and may run portable net10/core tests when the execution environment permits. This does **not** replace net48/WPF/TE2/Power BI Desktop/Fabric-tenant verification.

Read in this order:
1. `MASTER_PROMPT_V11_1.md`
2. `COMPARTMENTALIZED_ARCHITECTURE.md`
3. `CSHARP_AUTOMATION_IDE_SCOPE.md`
4. `AI_CONTEXT_EXPORT_SPEC.md`
5. `FABRIC_TOOLBOX_BOUNDARY.md`
6. `FEATURE_PROVENANCE_LEDGER.md`
7. `DEPENDENCY_UPDATE_MATRIX.md`
8. `APP_SWITCHER_AND_EXTERNAL_TOOLS.md`
9. `TESTING_DELEGATION.md`
10. `UPDATE_AND_VERSIONING_POLICY.md`
11. `ACCEPTANCE_GATE_V11_1.md`

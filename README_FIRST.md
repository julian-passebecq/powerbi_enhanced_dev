# PbiBench V6 — READ THIS FIRST

This pack is the implementation handoff for **PbiBench**, a Windows-first C#/.NET Power BI + Fabric engineering IDE.

## The architectural clarification

There is **one main product**:

```text
PbiBench.exe
   |
   +-- Model       -> our improved Tabular Editor 2 ("TE2++")
   +-- DAX         -> routine DAX lab/tests; bridge to standalone DAX Studio
   +-- Automate    -> typed bulk model actions + trusted C# macros
   +-- PBIP/Git    -> PBIP/TMDL/PBIR source engineering
   +-- Report      -> PBIR/report engineering later
   +-- Fabric      -> REST/XMLA/control-plane management
   +-- QA          -> BPA, DAX tests, report/model validation
   +-- Knowledge   -> Senior Playbook + SQLBI radar
   +-- Agent       -> MCP/AI orchestration later
```

**TE2++ is not a separate final product. It is the Model Editor inside PbiBench.**

The semantic-model engine is derived from the MIT-licensed Tabular Editor 2 source.
The PbiBench application shell, workflows, cloud control plane, report tools, Git, QA, knowledge, and automation are our own product.

## What should the first Codex pass render?

Not an empty architecture shell.

The first checkpoint must already be visibly useful:

- PbiBench starts on Windows.
- `Model` opens a working TE2-derived semantic editor.
- Existing TE2 model editing functionality is preserved.
- The Model page has our PbiBench shell/navigation.
- There is an Automation panel with several safe bulk actions.
- BPA runs and shows findings with preview/fix workflow.
- DAX can be formatted and sent to DAX Studio.
- A basic model relationship diagram exists.
- PBIP/Git/connection status is visible.
- No cloud or model mutation occurs without a preview/change plan.

## Bundled TE2 source

The pack contains an offline TE2 source snapshot under:

`vendor/TabularEditor2-bundled/`

The snapshot's internal version history reaches TE2 2.27.2.

As of the web research for this handoff, the current GitHub release is **TE2 2.28.0 (March 2, 2026)** and the repository was still being maintained in 2026.

Codex should:
1. build the bundled source first if offline;
2. if network access is available, fetch/pin TE2 2.28.0 before beginning substantial port work;
3. preserve TE2's MIT license and all third-party license notices.

The user does **not** need to upload TE2 again just to start.

## Reading order

1. `PASTE_THIS_TO_CODEX.txt`
2. `AGENTS.md`
3. `docs/MASTER_CODEX_PROMPT_V6.md`
4. `docs/ARCHITECTURE_LOCK_V6.md`
5. `docs/TE2PLUSPLUS_FIRST_PASS.md`
6. `docs/FEATURE_MATRIX_V6.md`
7. `docs/PASS1_TASKS_V6.md`
8. `docs/DAX_STUDIO_BOUNDARY_V6.md`
9. `docs/PBIP_TMDL_PBIR_V6.md`
10. `docs/TEST_AND_ACCEPTANCE_V6.md`
11. `docs/LICENSING_AND_SOURCE_BOUNDARIES_V6.md`
12. V5/V4/V3 docs for specialist subsystems.

Do not redesign the architecture before completing Pass 1.

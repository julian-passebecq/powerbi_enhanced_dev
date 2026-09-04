# PbiBench V7 — Pass 1.5 UX / Product Integration Pack

This is a **delta handoff** for the existing PbiBench V6 Codex project.

Do NOT replace the current repository with this ZIP.
Do NOT restart from the old V6 scaffold.
Do NOT refactor the working TE2 integration simply because this pack exists.

The current baseline already works:
- PbiBench launches
- TE2 2.28 is integrated in-process
- a model loads
- undo state is exposed
- DAX Studio is detected
- PbiBench owns the outer shell

That is now the protected baseline.

## Objective of this pass

Make the current application feel like **one coherent PbiBench IDE**, not a modern shell containing an embedded legacy TE2 window.

This pass is intentionally limited to:
- product identity / icon
- shell consistency
- TE2 chrome integration
- selection-aware inspector
- Automation UX
- BPA UX
- DAX Studio handoff polish
- model diagram polish
- PBIP/Git UX
- Home workflow
- scaling / regression / screenshots

Do NOT start full Fabric admin, PBIR authoring, Agent/MCP, DataForge, VizForge Studio, or DAX debugger work in this pass.

## Read in this order

1. `PASTE_THIS_TO_CODEX.txt`
2. `MASTER_PROMPT_PASS1_5.md`
3. `CURRENT_UI_AUDIT.md`
4. `UX_TASKS.md`
5. `COMMAND_INTEGRATION_PLAN.md`
6. `ICON_AND_BRANDING.md`
7. `AUTOMATION_AND_BPA_UX.md`
8. `ACCEPTANCE_GATE.md`

The screenshot under `reference/` is the working baseline to preserve.

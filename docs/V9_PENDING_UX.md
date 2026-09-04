# V9 staged — V7 functional prerequisite complete

**2026-09-05 update:** The user supplied and reviewed V7, then explicitly allowed taskbar appearance and pixel-level DPI appearance to remain manual visual QA pending. The V7 functional/structural gate is now complete: 390 test executions and all 15 original launch checks passed. See `V7_FUNCTIONAL_GATE.md`. Proceed sequentially with V9.1 onward; no screen-control access is required or requested. The text below records the original staging decision and is historical, not the current blocker state.

## User instruction and current status

The user supplied `pbibench_codex_v9_te3_like_semantic_ide.zip` as the next implementation contract, to be applied **after the current PbiBench UX pass is green**. On clarification, the user explicitly confirmed: **a separate UX pass is pending**.

Therefore V9 implementation has not started. Existing V6 build/test/smoke results do not satisfy the separate UX prerequisite. The pending UX pass or its task/contract still needs to be identified in this workspace.

## Staged contract

The supplied ZIP has been extracted without replacing the existing application or handoff files, under `contracts/V9/`. It contains the original V9 specifications, milestones, acceptance gate, manifest, and V8 reference material. These documents are requirements adopted by the user's request; they do not override the user's explicit sequencing instruction.

Read before starting:

1. `contracts/V9/README_FIRST.md`
2. `contracts/V9/MASTER_PROMPT_V9.md`
3. `contracts/V9/MILESTONES_V9.md`
4. The milestone-specific specifications
5. `contracts/V9/ACCEPTANCE_GATE_V9.md`

The existing working architecture stays fixed: PbiBench is the main C#/.NET application, TE2 is the integrated open semantic-model foundation, DAX Studio stays standalone, and all new mutations use the existing preview/approval/undo boundaries. Public TE3 documentation supplies capability requirements only; no proprietary implementation or assets are to be reused.

## Resume sequence

1. Identify and finish the separate UX pass, with its actual acceptance evidence.
2. Record its green result and preserve the working baseline.
3. Implement and validate **V9.1 DAX IDE Core**.
4. Follow **V9.2 Data Exploration**, **V9.3 Model Authoring Pro**, **V9.4 Automation / QA / Optimization**, **V9.5 Fabric / Refresh / Workspace**, then **V9.6 CLI / Agent / Compiler**, in order.
5. Stop at `contracts/V9/ACCEPTANCE_GATE_V9.md` with screenshots, tests, performance evidence and candid limitations.

No V9 feature code, architecture changes, or new dependencies have been introduced while this prerequisite is pending.

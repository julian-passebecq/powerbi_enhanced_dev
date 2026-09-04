# PbiBench V9 — TE3-like Semantic IDE Capability Pack

This is a **delta implementation pack** for the existing working PbiBench codebase.

Apply it after the current V7 UX pass is stable. It supersedes V8 as the main semantic-IDE roadmap; the useful V8 material is bundled under `v8_reference/`.

## Objective

Make the **TE2++ Model experience inside PbiBench** feel like a modern professional semantic-model IDE with most of the practical capabilities that distinguish TE3 from TE2 — while keeping an original PbiBench implementation and product identity.

This is not a request to clone TE3.

The target is:

> same broad developer problems solved, through PbiBench's own architecture and UI.

## Legal/source boundary

- Tabular Editor 2 is MIT/open source and may be forked/modified under that license.
- Tabular Editor 3 is proprietary.
- A TE3 license is a use license; it does not make TE3 source open source.
- Public TE3 documentation and observable feature descriptions can be used as a requirements/behavior reference.
- Do not decompile, copy proprietary implementation code/assets, or reproduce trade dress.
- Reuse only open-source code/components with compatible license terms and preserved notices.

This pack is an engineering plan, not legal advice.

## Product architecture remains locked

```text
PbiBench.exe
  |
  +-- Model        TE2++ semantic editor
  +-- DAX          PbiBench DAX IDE
  +-- Data         preview / profile / pivot lab
  +-- Automate     actions / scripts / AI
  +-- Diagram      semantic diagram
  +-- PBIP / Git   workspace / diff / sync
  +-- QA           BPA / tests / optimization
  +-- Fabric       Fabric / Direct Lake / import
  +-- Deploy       CLI / CI / refresh
  +-- Knowledge    guidance
  +-- Agent        safe tool-driven assistant
```

DAX Studio remains standalone for deep Server Timings/query-plan work.

## Read order

1. `PASTE_THIS_TO_CODEX.txt`
2. `MASTER_PROMPT_V9.md`
3. `FEATURE_PARITY_MATRIX.md`
4. `MILESTONES_V9.md`
5. `DAX_IDE_SPEC.md`
6. `DATA_EXPLORATION_SPEC.md`
7. `AUTOMATION_AI_SPEC.md`
8. `FABRIC_DIRECTLAKE_SPEC.md`
9. `WORKSPACE_DEVOPS_SPEC.md`
10. `MODEL_QUALITY_OPTIMIZATION_SPEC.md`
11. `CLEAN_ROOM_AND_LICENSE_BOUNDARIES.md`
12. `ACCEPTANCE_GATE_V9.md`

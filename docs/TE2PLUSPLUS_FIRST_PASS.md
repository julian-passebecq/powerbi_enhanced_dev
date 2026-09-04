# TE2++ First Pass Product Specification

## Goal

The first Codex delivery should feel like:

> "I opened a better Tabular Editor 2 inside PbiBench."

It should not feel like:
> "I opened a project planner that may someday contain an editor."

## Preserve from TE2 immediately

Do not regress:
- model explorer/tree,
- object property editing,
- measure editing,
- calculated objects,
- relationships,
- calculation groups/items,
- roles,
- perspectives,
- translations,
- display folders,
- dependencies,
- batch operations,
- copy/paste,
- undo/redo,
- C# scripting,
- BPA.

## Add visibly in Pass 1

### PbiBench shell
- light modern workbench
- left navigation
- current connection/project/Git header
- right inspector/findings area
- bottom output/log/test area.

### Automation
- safe typed actions
- exact change preview
- multi-select
- risk labels
- undo.

### BPA+
- finding cards
- explanations
- before/after
- safe fix subset
- link to object.

### DAX convenience
- formatting
- basic diagnostics
- dependencies
- "Open in DAX Studio".

### Diagram
- relationship graph
- active/inactive
- cardinality
- filter direction
- click-to-select.

### PBIP/Git awareness
- project root
- TMDL/PBIR indicators
- Git branch/status
- changed file count.

## Do not chase in Pass 1

- TE3 DAX debugger parity
- full PivotGrid
- full data preview engine
- full workspace mode
- all Fabric admin
- PBIR authoring
- AI agent.

## Quality bar

The app must retain TE2 reliability for semantic edits.
New UX must not hide destructive operations.
All new bulk edits are previewable.

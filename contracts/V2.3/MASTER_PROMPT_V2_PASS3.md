# Master Prompt — PbiBench V2 Pass 3

Baseline:
`70493e1a63064a7e6d2ec98c285d187a556834a3`

## Objective

Make the current broad functionality coherent to the user without undoing technical compartmentalization.

### Workstream A — ExternalTools boundary
Implement `EXTERNAL_TOOLS_BOUNDARY.md`.

This is P0 because the actual Report Studio project graph currently contradicts the module catalog.

### Workstream B — unified shell
Implement `UNIFIED_SHELL_UX.md`.

Do not rewrite the model editor or embed child processes.

### Workstream C — Design Exchange
Implement `DESIGN_EXCHANGE.md` and `THEME_AND_DASHBOARD_SPEC.md`.

Pass 3 validates/exchanges design metadata.
Do not attempt unrestricted AI-generated PBIR mutation yet.

### Workstream D — visual system
Implement `DESIGN_SYSTEM_AND_ICONS.md`.

Use a pinned legal SVG source such as Fluent UI System Icons, or original PbiBench vectors.
No Data Goblin assets/code.

### Workstream E — project context/package
Implement `PROJECT_CONTEXT.md`.

One PbiBench package, independently versioned components.

## Existing functionality to preserve
- V2 hosted CI
- Semantic View
- DAX Workbench
- Automation Gallery
- Report Studio Pass 2
- cross-layer report impact
- Fabric read-only report snapshot
- PBIR safety/recovery
- Workspace/Git
- DAX Studio/Bravo/Desktop/VS Code handoffs

## DAX Studio policy
No functional expansion.
Refactor only generic launcher infrastructure out of its project.

## Version
Suggested product version: `2.3.0`.

Suggested commit:
`v2-pass3 - Unified shell, design exchange and external-tools boundary`

## Testing
Targeted tests during development.
One final impacted Release gate before push.
Hosted CI runs after push.

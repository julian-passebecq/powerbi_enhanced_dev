# Report Studio Pass 2 + cross-layer impact

## UX depth
Add:
- search/filter by page, visual type/title/ID and semantic reference;
- page selector;
- zoom in/out, 100%, fit page;
- tree <-> wireframe <-> inspector <-> lineage synchronization;
- issue badges for schema errors, broken/unverified refs and hidden visuals;
- cached lineage per immutable ReportIndex snapshot rather than recomputing on each paint/click;
- explicit multi-report chooser;
- Open in Power BI Desktop / VS Code / Explorer where context is valid.

Do not attempt to render actual Power BI visuals.

## Safe report actions

All writes remain:
configure -> immutable ReportChangePlan -> exact diff -> review -> backup -> apply -> validate -> Git/disk.

P0:
1. Improve report-wide semantic reference mapping UI with occurrence/file/page/visual counts.
2. Duplicate/copy visual with optional bounded X/Y offset.
3. Batch visual visibility + common title show/text edits.
4. Bookmark duplicate/rename only for known pinned schemas.

P1 only if current schema/fixtures prove a narrow safe implementation:
5. Copy table/matrix header formatting.
6. Copy conditional formatting between compatible table/matrix fields.

If schema evidence is insufficient, implement detector/preview only. Do not guess.

## Display-name bridge

Do not let Report Studio edit TOM directly.

Use a versioned `pbibench-display-names.json`:
- qualified semantic field;
- display name;
- source report/page/visual;
- no values/tokens.

Report Studio can extract mappings.
Semantic IDE can import and preview annotation changes.
Report Studio can apply an explicitly reviewed mapping back to PBIR.

## Semantic impact review

When PBIP context exists:
- selected measure/column shows `Used in Reports (N)`;
- list report/page/visual;
- Open in Report Studio.

Before risky semantic rename/delete/refactor:
- show affected report usages;
- never silently change PBIR.

If a change needs both layers, produce a versioned impact/handoff plan. Apply TOM and PBIR separately with their own recovery guarantees. Do not claim atomicity.

# Report Action Gallery

PBIR report automation belongs in Report Studio, not in the old TE2 Trusted C# process.

## P0 safe actions

1. Duplicate page.
2. Duplicate/copy visual.
3. Copy visual between local PBIR reports.
4. Replace a semantic field/measure reference using explicit mapping.
5. Find broken report references.
6. Edit common visual title/display properties.
7. Add/update annotations.
8. Export report/page/visual inventory.
9. Validate PBIR.
10. Backup/restore reviewed changes.

## P1 useful actions

11. Copy conditional formatting between table/matrix fields.
12. Copy header formatting.
13. Store custom display names from visuals.
14. Apply stored display names.
15. Disable/bulk-edit visual interactions.
16. Reusable visual templates.
17. Reusable page templates.
18. Theme checks / standardization.
19. Bookmark helpers.
20. Find/replace fields across report.

## Why native PBIR actions instead of TE2 C#

Modern report automation can require newer C# language/runtime features and filesystem/report DTO logic.
Keep semantic C# in Semantic IDE.
Keep PBIR mutation in modern Report Studio.

A future modern Report C# host can be added as its own isolated module if it becomes valuable.

## Cross-layer actions

Later:
- create semantic integrity measures + reviewed diagnostic report page;
- model rename impact -> report reference updates;
- report usage -> safe semantic cleanup candidate list.

Use two-phase preview if both model and report layers change.

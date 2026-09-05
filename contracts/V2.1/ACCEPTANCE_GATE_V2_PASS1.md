# V2 Pass 1 acceptance

## Semantic View
- reuses/evolves existing diagram rather than duplicating it;
- active/inactive/cardinality/filter direction remain correct;
- cleaner table cards and toolbar;
- existing authoring/Undo behavior preserved.

## DAX
- existing query execution/history/results remain;
- DAX Studio handoff still works;
- Context/Help does not copy DAX Guide content;
- Quick Open routes to existing objects/editors.

## Bravo
- detect configured/installed Bravo;
- launch with supported server/database args;
- no fake feature-page deep link;
- disabled with clear reason when no compatible connection.

## C# Gallery
- curated cards with mode, selection, parameters, provenance and risk;
- no auto-run;
- Safe/native preferred;
- Trusted remains explicitly reviewed;
- MIT attribution retained for ported upstream code.

## Report Studio
- separate modern process;
- no TE2/App/ModelEditor/TOMWrapper dependency;
- opens PBIP/PBIR;
- report tree;
- page wireframe;
- inspector;
- raw JSON read-only;
- schema/version awareness;
- exact diff;
- stale-hash rejection;
- backup/restore;
- atomic file writes;
- validation after apply.

## Report actions
At least four typed actions work end to end and preview exact files.

## Lineage
- measure/column -> report/page/visual usage;
- visual -> semantic references;
- broken refs explicit.

## Module/provenance
- new Report Studio/PBIP/PBIR/lineage/Bravo bridge entries;
- update lanes explicit;
- third-party source/license recorded.

## Tests
- targeted unit tests;
- Report Studio offline smoke;
- Semantic IDE smoke;
- external tool launch argument tests;
- one final impacted Release gate.

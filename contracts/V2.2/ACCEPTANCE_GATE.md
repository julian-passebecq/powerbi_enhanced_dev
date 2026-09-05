# V2 Pass 2 acceptance gate

- V2 tests run in hosted CI.
- Report Studio build/isolation run in hosted CI.
- TMDL indentation/completeness bug has regression tests.
- Gallery implementation origin is distinct from reference source.
- stale future-report-tooling placeholder is removed/redefined.
- V2 Pass1 verification has truthful post-push CI note.
- PBIR write compatibility is tied to an explicit pinned supported-version/schema policy, not a permanent exact `4.0` string check.

Report Studio:
- search/filter;
- page navigation + zoom/fit;
- synchronized tree/wireframe/lineage;
- cached lineage;
- issue badges;
- report mapping shows occurrence impact;
- bounded visibility/title batch action;
- bookmark edits only for recognized schemas;
- all writes still exact-plan/backup/validate/restore.

Cross-layer:
- measure/column -> report usages;
- open usage in Report Studio;
- risky semantic refactor shows report impact;
- no silent PBIR write;
- no atomic TOM+PBIR claim.

C# Gallery:
- corrected provenance;
- advanced templates are drafts only;
- no Trusted auto-run;
- native/Safe preferred.

Fabric snapshot:
- explicit getDefinition only;
- 200/202/429 handled;
- strict part-path and payload bounds;
- new snapshot directory only;
- no tokens/credentials persisted;
- PBIR-Legacy read-only;
- no updateDefinition.

Regression:
- V11 portable regressions green;
- V2 tests green;
- relevant net48/WPF tests green locally;
- Report Studio/Fabric Toolbox isolation green;
- one final impacted Release gate passes.

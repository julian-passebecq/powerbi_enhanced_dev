# Pass 3 testing delegation

## During Codex implementation
Targeted only:
- ExternalTools/process argument tests;
- actual project-reference/module-catalog closure tests;
- model-context export tests;
- dashboard-spec validation;
- theme validation;
- focused shell WPF tests;
- child process handoff tests.

Do not rerun all historical suites after each edit.

## Final Codex gate
Run one impacted Release gate:
- changed project builds;
- V2 tests;
- relevant V11 portable regressions;
- net48 shell tests;
- Report Studio build/isolation;
- Fabric Toolbox isolation;
- package/component manifest smoke.

## After push
Hosted CI should run independently.
External reviewer will inspect:
- exact project graph;
- CI result;
- design-contract security;
- no accidental DAX Studio expansion;
- module/provenance consistency.

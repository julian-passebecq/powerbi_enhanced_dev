# Testing delegation and token-efficiency policy

## Goal

Spend Codex reasoning on implementation and Windows-only verification. Move repetitive regression work to deterministic tests/CI and post-push review where possible.

## Codex must run locally before push

For every changed area:
- build changed projects;
- focused unit tests for changed modules;
- a compile of the main PbiBench solution if shared contracts changed.

Additionally run only when relevant:
- native TE2/net48 tests when ModelEditor/TOMWrapper/Undo/native Semantic boundaries change;
- one WPF/WinForms smoke when desktop UI/navigation/editor hosting changes;
- Fabric adapter tests when Fabric contracts/transports change;
- packaging/runtime smoke when output layout/dependencies change.

Do not skip a test merely because post-push review exists. The post-push reviewer cannot replace Windows/net48/native runtime validation.

## Full historical regression policy

Do not rerun every historical suite after every small edit.

Use this escalation:
1. **edit loop** -> changed-project build + focused tests;
2. **feature complete** -> impacted subsystem gate;
3. **before push** -> one final relevant gate;
4. **full repo gate** -> only when shared Core/TE2/native serialization/Undo/runtime packaging changed substantially, or when preparing a release.

This should reduce token/time waste while keeping a clear safety rule.

## Make portable tests reviewable outside the Windows host

Where practical, keep these components pure/no-WPF/no-process-global-TE2-state and dual-target `net10.0;net48`:
- AI Context Export planning/serialization/redaction/checksum logic;
- provenance parsing/validation;
- C# language-service lexical/completion/signature metadata where feasible;
- macro/snippet data contracts;
- Fabric request/response DTOs and pure planners.

This allows an external post-push reviewer to run `net10.0` tests if the source checkout/runtime is available.

## Post-push review done outside Codex

After the push, ChatGPT can inspect GitHub directly for:
- exact commit/diff;
- architecture/project-boundary violations;
- provenance/dependency correctness;
- missing test cases;
- unsafe secret/data export paths;
- Safe Preview vs Trusted C# separation;
- third-party dependency/license implications;
- CI/status results;
- portable/core tests where the available execution environment supports them.

Codex does **not** need to spend another long pass narrating its own architecture or repeating a repository-wide audit after it pushes.

## Tests ChatGPT cannot be assumed to run

Do not delegate these away from the Windows/Codex environment:
- actual net48 WPF/WinForms launch behavior;
- hosted TE2 process-global/native behavior;
- Windows DPI/taskbar/manual visual checks;
- Power BI Desktop local Analysis Services integration;
- authenticated Fabric tenant operations;
- tests requiring the user's Windows credentials or installed desktop tools.

## CI as token saver

If feasible, add a `windows-latest` GitHub Actions fast gate. Prefer deterministic commands/scripts already used locally. A useful split is:
- push/PR fast gate: restore/build + core/adapters/language tests + main compile;
- manual/release full gate: native/packaging/smoke suites.

Do not introduce fragile CI-only dependency hacks merely to check a box. If CI cannot yet build the pinned TE2/runtime cleanly, keep the workflow narrow and document the excluded Windows-native gate explicitly.

## Real integration remains separate

A later dedicated pass with a selected PBIX/PBIP/populated model and Fabric environment should validate:
- DAX execution;
- Data Preview/Profile/Pivot;
- UDF/Calendar semantics;
- VertiPaq/DMVs;
- semantic assertions;
- refresh;
- Disk/Live/Git sync;
- Fabric auth/import/Direct Lake.

Fixtures must remain labeled fixtures.

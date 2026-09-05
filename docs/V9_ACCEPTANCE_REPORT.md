# V9 acceptance report

**V9 functional implementation and automated verification are complete.** All six milestones ran sequentially. Work stops here at `contracts/V9/ACCEPTANCE_GATE_V9.md`; full external integration acceptance remains pending the live checks listed below.

PbiBench remains the main C#/.NET desktop application with the existing in-process TE2 2.28 Model Editor. DAX Studio remains standalone. The V7 functional/structural gate is complete. Windows taskbar-icon appearance and pixel-level appearance at 100/125/150% DPI remain **manual visual QA pending**, nonblocking per the user's instruction.

## Capability evidence

Release verification: **1,276 test executions, 68 application launch checks and 25 CLI launch checks; zero failures or skips.** The PbiBench build has 0 warnings/errors. The pinned upstream TE2 build retains its documented reference warnings.

The portable package is `artifacts/PbiBench-V9-20260905/` (GUI: `PbiBench.exe`; CLI: `cli/pbibench.exe`). A distributable archive is available as [PbiBench-V9-20260905.zip](../artifacts/PbiBench-V9-20260905.zip), 27,228,462 bytes. It independently passed the same 68 + 25 launch checks. All 624 files in its checksum manifest match; the ZIP contains those files plus the manifest. The packaged CLI also passed the four-step read-only CI workflow. Evidence paths and per-suite counts are in `V9_IMPLEMENTATION_STATUS.md` and `artifacts/v9-6-functional-evidence/`.

| Area | Implemented and exercised | Boundary |
|---|---|---|
| DAX IDE | Metadata completion, tolerant diagnostics, signatures, Go/Peek/navigation history, UDF syntax, code-action diffs, query documents, partial execution, multiple result grids, history and DAX Studio handoff | Editor diagnostics complement the Microsoft engine. Populated-engine execution remains unverified. |
| Data | Independent table previews, virtualized results, bounded paging/fallback, profiles, relationship coverage and Pivot Lab layouts/tests | Engine capabilities and complete-key checks gate paging. Fixture evidence is labeled; live profiles/Pivot results remain pending. |
| Model authoring | DAX Scripts diff/apply, UDF and Calendar workspaces, editable perspectives/translations, Table Groups, diagram selection and relationship editing | Compatibility checks block unsupported metadata without automatic upgrades. Live UDF/calendar semantics remain pending. |
| Automation | Interpreted Safe C# Preview, separate Trusted Legacy execution, action recorder, macro library and versioned BPA packs | Safe Preview accepts the documented subset. Trusted Legacy retains explicitly acknowledged unrestricted scripting. |
| Quality | BPA preferences/suppression, VPAX import, public-DMV capture, optimization signals, semantic assertions/snapshots, cancellable background queue | Imported files and synthetic transports prove local behavior; successful populated-engine metrics and benchmarks remain pending. |
| Fabric and refresh | Public Fabric/OneLake/SQL discovery and preview, local import/conversion/schema reviews, Direct Lake guards, advanced refresh plans and private execution sessions | Real authentication, Fabric source reads, processing and remote writes require external integration validation. |
| Workspace | Disk/Live/Git comparison, watcher invalidation, semantic Git diff, conflict review, guarded push/pull and recovery snapshots | Remote writes are private transactions against reviewed targets. Metadata backups do not recover processed data. |
| CLI and CI | Own CLI with typed JSON commands, profiles, predictable exits, noninteractive reads, local writes, refresh/deployment review, and CI artifacts | Local writes export reviewed BIM destinations. Remote reviews are single-use and expire. Engine assertions require an accessible target. |
| Agent | Offline context review/templates, strict typed proposals, shared command preview/apply, query/test staging and optional OpenAI Responses adapter | Default offline; context sharing is explicit. No generated text can grant approval. Provider tests use synthetic HTTP. |
| Prototypes | Metric View YAML intent/diagnostics and reviewed aggregate proposals; local hash-pinned DAX packages, dependencies, version/license/lock review and native Undo | Bounded original prototypes. SQL/DAX equivalence, arbitrary YAML, remote package feeds and full compiler/package-manager compatibility are not claimed. |

Milestone build/test/launch counts and source references are retained in `V9_IMPLEMENTATION_STATUS.md`. Detailed behavior and exclusions are in the `V9_*_REFERENCE.md` documents.

## Generated screen captures

These images come from the actual instrumented WPF/native application launch. They were inspected from local files and do not replace Windows taskbar or DPI visual QA.

![Agent shared command preview](../artifacts/build/smoke-Release-d4c73eb307ce406a9db7b7259401da06/agent-preview.png)

![Semantic compiler and explicit table mapping](../artifacts/build/smoke-Release-d4c73eb307ce406a9db7b7259401da06/semantic-compiler.png)

![Local DAX package review](../artifacts/build/smoke-Release-d4c73eb307ce406a9db7b7259401da06/dax-packages.png)

The same capture folder includes the preserved Model Editor, DAX, Data Preview, Pivot Lab, model-authoring editors, automation, BPA, VertiPaq, Fabric, refresh and workspace screens.

## Performance evidence

- The 20,000-symbol language-service fixture completed analysis and completion in 31 ms on net10 and 27 ms on net48, returning 400 visible suggestions (`artifacts/v9-language-tests/`).
- The WPF fixture materialized and laid out 10,000 rows × 12 columns in 1,272 ms, with 27 realized rows. This measures UI virtualization on this machine, not engine query performance.

## Source and license boundaries

The TE2 foundation remains pinned to `75f10e331b8de0dda5c213180b9b8867b4a38191`. Original licenses, third-party notices and integration patches are preserved. The bounded Function-only Undo ordering correction and its regression are described in `V9_PROTOTYPES_REFERENCE.md`. Both runtime assemblies match the freshly built upstream outputs. The package process collects notices from both GUI and CLI dependency assets. New compiler/package/Agent implementations use public behavior documentation and public Microsoft/OpenAI interfaces. No proprietary TE3 code, UI assets or internal implementation was copied or decompiled.

## Remaining validation and optional work

No populated model catalog was available during the read-only endpoint probes. Live query semantics, paging/profiles, UDF/calendar behavior, DMV metrics, semantic assertions, Fabric authentication/preview/import, refresh and live/disk writes are not represented as proven by offline fixtures. The optional provider also needs a configured API account/model for a live request. These are external integration limits, separate from automated local checks and the two manual V7 appearance checks.

Stop at this V9 gate. Later optional scope includes a full DAX debugger, broader semantic compiler and package feeds, localization, and broader Report/Knowledge workspaces. Those are not silently implemented as part of this acceptance pass.

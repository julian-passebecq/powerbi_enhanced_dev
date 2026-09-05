# PbiBench V11.1.1

V11.1.1 hardens baseline `47954aaf8876671eff879bbd0b809660f444e1fe`. The original V11.1 delta specification is retained under `contracts/V11.1`. This corrective pass covers C# recovery/file conflicts, export review invalidation/redaction transparency, completion escaping, and Release/CI verification. Existing V9/V11 functionality and process boundaries are preserved.

## Applications and dependencies

PbiBench.exe remains the Semantic IDE on net48 with TE2 2.28.0 at `75f10e331b8de0dda5c213180b9b8867b4a38191`. No TE2 source patches or runtime migration were introduced. The two existing TE2 patches and original upstream licenses remain unchanged.

Apps / Tools launches Fabric Toolbox, configured DataForge, Power BI Desktop, VS Code and optional Codex as external processes. DAX Studio retains its current query/connection handoff. Configure missing executables in the switcher. DataForge remains an external companion: this pass uses the existing versioned contract reader and does not invent a second data generator.

Fabric Toolbox is a separate net10.0-windows executable. It references the existing shared Fabric service layer and no Semantic/ModelEditor/TE2 UI assembly. Workspaces offers paginated all-item inventory; OneLake / Data uses shared source schema and bounded SQL preview. Settings authorizes Fabric, OneLake and SQL separately through the existing in-memory MSAL provider. Operations describes the deferred platform scope; it does not offer unimplemented execution buttons. No large admin feature expansion was attempted.

Export a `.pbifabric.json` selection in Toolbox and import it through Apps / Tools in Semantic IDE. The strict version-1 envelope carries selected identifiers and labels only. The user then signs in and loads the catalog before reviewing semantic import. Handoff files cannot carry credentials, commands or approval. The original Fabric page remains as the semantic import/compatibility surface.

Feature ownership, source class, exact pins, adapters, tests, local patches and update lanes are in `docs/architecture/provenance.json`, embedded in Core and displayed by About. MSAL 4.84.2 and SqlClient 6.1.6 remain unchanged. The C# adapter reuses the existing FCTB 2.16.24 binary with its LGPL notice. No Roslyn package was introduced. Dependency/source inventories and upstream source availability obligations remain part of packaging.

## AI Context Export

Use Apps / Tools → AI Context Export with a model open. Choose full metadata or selected roots (including the current tree selection). Explicit exclusions override dependency expansion. Unchecking a table in full-model mode excludes its children. The separate Exclude column also works in selected mode. Dependencies and required table/relationship context are recorded; unresolved references caused by exclusions remain explicit.

Samples are OFF. The Samples per table tab accepts bounded custom row counts (0 excludes a table), hidden-column opt-in and an optional selected order column. The Scope tab controls individual sample columns. Before fetching rows, enable sampling and acknowledge the sensitive query/data review. First-N queries project only reviewed columns and use an explicit sort plus a client row cap. Ties and incomplete representativeness are recorded. Source work is not bounded by returned row counts for DirectQuery/Direct Lake.

Review files prepares detached UTF-8 JSON/CSV/DAX and a human-readable AI README. Export ZIP requires a second acknowledgment of the exact captured files. Configuration edits invalidate that review. Default bounds are 250 rows/table, 100,000 total cells and 32 MiB; hard caps are 1,000 rows/table, 1,000,000 cells, 200 columns/sample and 128 MiB. Cancellation and size failures preserve any previous destination file. Deterministic manifest ordering, fixed ZIP timestamps and SHA-256 checksums make identical captures reproducible; live sample contents may differ across queries.

V11.1.1 binds invalidation to checkbox state, observable scope/sample properties and limit text changes, including programmatic changes. Use current tree selection always clears the plan, acknowledgement, file list and content preview, even if selected scope was already enabled. Configuration changes cancel pending preparation; a version check prevents stale completion from publishing a plan. Export commits pending grid edits and rechecks review after the destination dialog.

The manifest now has envelope `schemaVersion: 2` with the existing `data` and a sibling `redaction` object (`schemaVersion: 1`). It records `applied`, `replacementCount`, sorted per-file `files` counts, the counting unit, and `anonymized: false`. Counts measure path/credential pattern replacements, including repeated occurrences, samples/evidence, the README and the manifest itself. Counts retain no original values; zero redactions is explicit. Other JSON file envelopes remain version 1, and checksums cover the final manifest bytes.

Explicit metadata projection excludes connections, credentials, annotations, role members, source/partition expressions and local recovery paths. Calculated-table partition definitions are also omitted deliberately; calculated columns, measures, calculation items/groups and UDF DAX are included. This omission is recorded in the manifest. Text values receive conservative path/obvious-credential redaction; arbitrary business text is not proven anonymous. Names, DAX and rows can still be sensitive. Samples are explicitly labelled **not anonymized**. No AI provider is contacted.

Optional RLS filters exclude identity membership. Optional evidence uses only current captured BPA object findings, matching-name VertiPaq table statistics, semantic test outcomes without result values, and presentation-property workspace change indicators without raw paths/file contents. Available evidence is exported as captured; unavailable diagnostics are not fabricated. Model-wide test/workspace indicators are omitted for selected scope. Row counts are unknown unless present in included diagnostics. The automation reference is generated from the actual safe recipe scope/operation/property contracts; local macros and trusted source text are never automatically exported.

The existing strict proposal import remains unchanged: imported files cannot approve themselves.

## Practical C# automation

The existing Safe C# Preview and Trusted Legacy tabs each host a multi-document workspace. New/Open/Save/Save As support `.csx` and `.cs`, with unsaved markers and bounded recovery in the user profile. Recovery schema 2 also reads legacy schema 1; loading either restores visibly detached drafts, strips write baselines and retains the old path only as an advisory tooltip. All recovered tabs require saving, even previously clean or empty tabs, and execution trust is never restored. Limits remain 24 tabs, 1 MiB UTF-8 per script and 8 MiB per recovery file.

Open captures SHA-256 from the exact bytes decoded into the editor. Save hashes the destination again after staging the new contents, immediately before atomic replacement. Changes to bytes (even with unchanged size/timestamp), deletion and unverified existing destinations fail closed. The conflict dialog offers Reload (explicit draft discard), Save As, Overwrite and default Cancel. Overwrite is bound to the observed hash and path and rechecks both; another change requires another decision. A lock fails without deleting the existing file or retrying against an unchecked version. New destinations use a non-overwriting move. Atomic replacement is not an operating-system compare-and-swap: an uncooperative writer replacing an existing path in the narrow interval after the final hash read remains a concurrency limitation.

The existing native editor supplies syntax coloring, line numbers, bracket highlighting, indentation, find/replace and comment/uncomment. Ctrl+N/O/S and Ctrl+Space supplement toolbar commands. Model/Selected completion prioritizes common semantic members and exact loaded object names. Ordinary string-literal completion escapes quotes, backslashes and control characters; spaces, brackets and Unicode are preserved. Escaped table receivers and partially typed escapes are covered by regression tests. Completion stays capped at 200 results; the existing TE2 compiler validates integration test insertions without executing them. Call tips and labelled original snippets do not execute code.

The pure `PbiBench.CSharp.LanguageService` dual-targets net10/net48 and has no WPF, compiler runtime, TE2 state or execution authority. It uses bounded lexical/semantic metadata assistance; it does not claim general CLR completion. TE2 compiler diagnostics remain authoritative for full C#. Compile / review risks validates without invocation and displays line/column errors and advisory filesystem/network/process/registry/reflection/interop/loop hints. Then acknowledge trust and run the unchanged snapshot/native execution boundary. Source or document changes reset trust. No findings is not a safety proof. External effects remain unrestricted and non-undoable.

The recorder keeps typed recipes as the replay format and offers generated C# text/export. Unsupported recording notices are carried into the generated text, and unrepresentable recipes fail explicitly. Macro search covers names/tags/mode; favorites sort first. Optional fields preserve the version-1 library format. No debugger, project system, NuGet UI or Visual Studio replacement is included.

## Verification and packaging

During development, use the changed-project builds and targeted tests. The final impacted Windows gate is:

```powershell
./scripts/invoke-v11-gate.ps1 -Configuration Release
```

It requires the existing pinned TE2 build outputs (`build-pass1.ps1 -SkipTests -SkipUpstreamTests -SkipSmoke` bootstraps these if absent), compiles the solution, runs V11 tests on net10 and net48, targeted native scripting/model boundaries, Fabric adapter tests, capture/preview tests, and targeted WPF tests. It packages into a fresh artifacts directory and runs the V11 Semantic IDE and Toolbox process smokes against the package. Runtime JSON/dependencies remain in `fabric-toolbox/`; they are not copied into the net48 host directory. The Toolbox requires the .NET 10 Windows Desktop runtime; the Semantic IDE requires .NET Framework 4.8. The package is framework-dependent.

Release is now the gate default; Debug remains available explicitly. Every gate uses a fresh configuration-labelled artifact directory, records the console transcript and TRX results, verifies Toolbox output isolation, and emits `gate-result.json` only after both process smokes succeed. `-SkipPackaging` is marked explicitly in that result and does not count as a packaged Release gate.

The existing **V11 portable and Fabric fast gate** workflow remains unchanged. The additional **V11 Windows Release module gate** runs V11 tests on net10 and net48 in Release, builds Toolbox in Release, checks output isolation and uploads TRX evidence. These hosted module tests do not require the prebuilt TE2 toolchain. They exclude main WPF/native TE2 execution, GUI/package smoke and live PBIX/Power BI/Fabric validation; the local Release gate covers the native/package checks. No private credentials are required. Fixtures and generated smoke context are synthetic and stay in ignored artifacts.

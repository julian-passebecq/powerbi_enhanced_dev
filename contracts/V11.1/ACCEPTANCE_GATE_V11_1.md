# V11.1 acceptance gate

V11.1 is complete when all applicable items below are true.

## Preserve the Semantic IDE

- Existing TE2 2.28 model editor remains available and working.
- DAX IDE/query, Data Preview/Pivot, authoring, automation, QA/VertiPaq and workspace entry points remain available.
- DAX Studio handoff remains available when installed.
- No host/runtime migration was introduced merely to support C# editor assistance or compartmentalization.

## Provenance/update ownership

- repository contains `docs/architecture/FEATURE_PROVENANCE_LEDGER.md`;
- repository contains `docs/architecture/DEPENDENCY_UPDATE_MATRIX.md`;
- `docs/architecture/provenance.json` validates;
- About/Provenance displays ownership/source class/version or pin;
- TE2 local patches are explicit;
- Fabric, TE2, DAX language service, external-tool bridges and PbiBench-owned modules have separate update lanes.

## Fabric Toolbox boundary

- separate Fabric Toolbox executable/project exists;
- it reuses shared `PbiBench.Fabric` services rather than duplicating clients;
- it does not reference TE2/ModelEditor UI assemblies;
- Semantic IDE can launch it;
- broad Fabric workflows are clearly owned by Toolbox while semantic-model-specific Fabric hooks remain in Semantic IDE;
- existing Fabric behavior is not destructively removed before equivalent replacement is proven.

## AI Context Export

- exports full or selected semantic scope;
- sample data is OFF by default;
- user can select tables/columns and row counts;
- export preview lists included categories and estimated size;
- credentials/tokens/connection strings/secret-bearing source fields are excluded by construction and tests;
- ZIP contains deterministic manifest, human-readable AI README, machine-readable model data and checksums;
- optional automation reference is generated from actual current capabilities;
- exporter is cancellable and bounded;
- sensitive sample-data warning is visible;
- optional proposal import remains strict/preview-only and cannot self-approve.

## Practical C# automation UX

At minimum:
- multi-tab script workspace or equivalent coherent document model;
- open/save/recovery/unsaved state;
- syntax highlighting/line numbers/search/bracket/indent basics;
- model/selection-aware completion for common automation APIs;
- signature/call help for common semantic scripting methods;
- compile diagnostics shown before Trusted C# execution;
- useful original semantic snippets;
- clear Safe Preview vs Trusted C# execution choices;
- advisory risk hints for obvious high-impact Trusted APIs;
- recorder retains typed Recipe and can show/export generated C# for supported operations;
- Macro Library has at least search/filter and mode visibility;
- no C# project system/debugger/NuGet IDE was introduced.

If a Roslyn dependency creates a net48/TE2 binding conflict, acceptance does not require forcing Roslyn. The language-service abstraction and useful completion/diagnostics must still exist using a compatible implementation.

## App/Tool clarity

- Semantic IDE, Fabric Toolbox, AI Export, DataForge and external tools are visibly distinguished;
- missing companion/external executables fail gracefully;
- provenance/about identifies which component owns the current feature.

## Testing

- changed projects build;
- focused changed-module tests pass;
- relevant native/WPF tests run only where those boundaries changed;
- one final impacted gate passes before push;
- no false claim of live PBIX/Fabric integration;
- if CI was added, its scope/exclusions are explicit and the workflow is not represented as covering tests it does not run.

## Stop

Do not add a C# debugger/project system, full DAX debugger, PBIR/report engineering, broad semantic compiler/package expansion or a large Fabric-admin feature wave in this pass.

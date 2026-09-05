# Master Prompt — PbiBench V11.3

Baseline: `4caccae9f4751555cbe584ffbf02e81e2fb88f77`

Objective: continue functionality while making module/update boundaries explicit enough that TE2, Fabric, external tools and PbiBench-owned subsystems evolve independently.

## A. Correct Feature Map lifecycle
Remove `Freeze` as a development-mode value.
Use Active / Selective / Independent / Incubating / On demand / Later.
Do not imply Labs/Future/Companion features are prohibited from future development.
Update catalog schema/runtime validation/UI copy/generated docs/tests.

## B. Add module catalog
Implement `docs/architecture/module_catalog.json` plus pure Core parser/validator.
Feature rows reference module IDs.

At minimum define:
semantic-ide, dax, csharp-automation, fabric-services, fabric-toolbox,
ai-context-export, daxstudio-bridge, dataforge-bridge, agent,
semantic-compiler, dax-packages, future-report-tooling.

Validate IDs, lifecycle/kind, dependencies, acyclic graph, owner/update lane/tests, framework metadata, and forbidden dependencies.

Feature Map details should show module/version/update lane/process/runtime.

## C. Fabric Toolbox V0.2
Implement `FABRIC_TOOLBOX_V02_SCOPE.md`:
- item search/filter;
- item details;
- safe inventory export;
- read-only recent job/operation view using verified public APIs;
- bounded pagination/cancellation;
- functional Operations page;
- no job write actions.

Keep Toolbox free of TE2/ModelEditor/Semantic UI dependencies.

## D. C# automation
Implement `CSHARP_AUTOMATION_NEXT_SCOPE.md`:
- compiler Problems panel and line/column navigation;
- bounded macro context/enable rules;
- selection-aware semantic snippets;
- no automatic Trusted execution.

Preserve Safe vs Trusted boundaries and V11.1.1 file/recovery safety.

## E. Versioning
Suggested:
- product 11.3.0
- Fabric Toolbox 0.2.0
- independent module versions in module catalog.

Commit:
`v11.3 - Modular growth, Fabric Toolbox v0.2 and C# automation`

## F. Do not couple unrelated updates
No TE2 upgrade because Fabric changed.
No Fabric dependency upgrade because C# changed.
No net48 migration.
No merging Toolbox into Semantic IDE.
No embedded DAX Studio.
No full DAX debugger in this pass.

These are sequencing rules, not permanent prohibitions.

## Testing
Targeted tests while coding; one relevant Release gate before push; post-push audit delegated to separate reviewer.

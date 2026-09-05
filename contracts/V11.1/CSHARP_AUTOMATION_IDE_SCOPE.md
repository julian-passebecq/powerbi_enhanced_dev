# Practical C# Automation IDE scope

C# remains useful in PbiBench because it is the fastest escape hatch for bulk semantic-model automation. The goal is **not** to build Visual Studio. The goal is to make short Tabular/TE2 automation scripts pleasant, discoverable and safer to review.

## Product principle

Keep two execution lanes:

1. **Safe Preview** - the existing restricted PbiBench C#-shaped grammar -> typed `ActionRecipe` -> detached TOM diff -> explicit Apply -> one Undo batch.
2. **Trusted C#** - the existing full TE2 scripting engine for real C# power-user scripts. It is not a security sandbox and external side effects cannot be undone reliably.

Editor assistance must not blur those security/execution boundaries.

## P0 - implement now if low-risk

### 1. C# Script Workspace

Add a coherent script workspace rather than a single basic textbox:
- multiple script tabs;
- New/Open/Save/Save As for `.csx` and `.cs` text files;
- unsaved marker and recovery of unsaved text;
- line numbers;
- search/replace;
- bracket matching;
- auto-indent;
- comment/uncomment;
- sensible keyboard shortcuts;
- output/diagnostics pane;
- clear `SAFE PREVIEW` vs `TRUSTED C#` badge per execution action.

Do not create a C# project/solution system.

### 2. Semantic-model-aware completion

The highest-value completion is not generic CLR noise. Prioritize the automation surface:
- `Model`;
- `Selected`;
- TE2/TOMWrapper table/column/measure objects;
- common semantic properties (`Name`, `Description`, `IsHidden`, `DisplayFolder`, `Expression`, `FormatString`, etc.);
- common authoring methods such as measure creation;
- loaded model table/column/measure names when context is unambiguous.

Completion should show member kind, short signature and a short description where available.

### 3. Signature help / call tips

Show useful parameter information for common scripting methods. At minimum cover the model/selection APIs and common creation/editing helpers used in actual automation scripts.

### 4. Compile diagnostics before Trusted Run

Before running Trusted C#, compile/validate first and show line/column diagnostics. Do not execute if compilation fails.

Prefer an isolated Roslyn-backed language service **only if it integrates cleanly with the current net48 host and does not destabilize TE2 dependencies**. Keep it behind `ICSharpLanguageService` or equivalent. If current Roslyn packages cause runtime/binding conflicts, do not force an app-wide dependency migration: retain the interface, use the existing TE2 compiler diagnostics plus reflection/metadata-backed completion, and leave richer Roslyn hosting for a later isolated helper process.

### 5. Useful snippets/templates

Ship a small original snippet library for high-frequency semantic work, for example:
- create SUM measures from selected numeric columns;
- set `SummarizeBy=None` on selected columns;
- move selected measures to a display folder;
- hide selected technical/key columns;
- add descriptions;
- create a measure table;
- bulk format selected measures;
- basic time-intelligence measure template;
- selected-object loop template.

Snippets insert text; they never execute automatically.

### 6. Script risk hints

For Trusted C#, add an **advisory** static risk scan before execution. Flag obvious use of high-impact APIs/categories such as:
- filesystem;
- network/HTTP/socket;
- process execution;
- registry/environment mutation;
- reflection/dynamic assembly loading;
- native interop;
- long/unbounded loop patterns where detectable.

This is a review aid, **not a sandbox or security proof**. Never label a script "safe" merely because the scanner found nothing.

### 7. Recorder -> Recipe and C# view

Keep the typed Action Recipe as the reliable replay format. Add an optional readable **Generated C#** view for supported recorded operations so users can learn/edit/export the equivalent automation.

Generated C# must be treated as text until the user explicitly previews/runs it. Unsupported recorded operations remain explicit; do not silently omit them from a script while claiming completeness.

### 8. Improve Macro Library usability

Without creating a second script-management system, add lightweight:
- search/filter;
- tags/category;
- favorite/pin;
- mode badge (`Recipe`, `Safe Script`, `Trusted C#`);
- last-used timestamp if already compatible with the current storage format or via a versioned migration.

## P1 - useful later, not required to block this pass

- hover/quick documentation for common automation APIs;
- format document/selection;
- code folding if the selected editor control supports it cheaply;
- outline of methods/classes for larger Trusted scripts;
- more complete generic C# completion;
- trusted `#r`/assembly-reference UX with explicit warning;
- richer API navigation;
- parameterized macros with typed user inputs.

## Explicitly later / low value

Do not spend this pass on:
- C# debugger, breakpoints, locals/watch/call stack;
- `.csproj`/solution/project system;
- NuGet package manager/package restore UI;
- MSBuild authoring;
- general C# refactoring suite;
- C# profiler/code coverage;
- C# unit-test runner;
- GUI designer;
- arbitrary extension marketplace;
- Visual Studio parity.

## AI Context Export integration

The AI export should optionally include an **automation reference** so any external AI can generate a useful script without an embedded chat provider:

`automation/README.md`
- explain `Safe Preview` vs `Trusted C#`;
- list supported Safe Preview constructs;
- describe the main `Model` / `Selected` automation patterns;
- show 3-6 small original script examples;
- tell the AI to prefer a Safe Preview-compatible script when possible and to label when full Trusted C# is required.

`automation/safe-script-capabilities.json`
- machine-readable supported targets, properties and operations from the actual current code/schema, not a hand-maintained fiction.

Optionally include a bounded snapshot of selected model object names/types so generated automation can reference real objects.

## Architecture boundary

Prefer a separate project such as `PbiBench.CSharp.LanguageService` for editor intelligence. Keep it free of WPF and process-global TE2 state where practical. A pure dual-target (`net10.0;net48`) service is preferred because it is easier to unit-test independently and can later be reused by CLI/helper processes.

The UI adapter belongs in `PbiBench.App`; execution remains in the existing Automation/ModelEditor boundary.

# V9.4 script automation reference

PbiBench keeps its integrated TE2 2.28 model editor and undo infrastructure. Safe C# Preview, Trusted Legacy, Action Recorder and Macro Library are separate tabs in the Automation scripting experience. This document describes the implemented surface and its limits, rather than full C# compatibility.

## Safe C# Preview

Safe Preview interprets an original, allowlisted C#-shaped grammar into a typed `ActionRecipe`. It does not compile user source, resolve CLR types, use reflection, or invoke arbitrary members. Filesystem, network and process APIs have no representation in that interpreter. Loading or saving a script through the application's file commands is a separate, explicit application operation.

Supported targets are:

| Form | Meaning |
| --- | --- |
| `Model.Tables["Sales"]` | One table |
| `Model.Tables["Sales"].Measures["Revenue"]` | One measure |
| `Model.Tables["Sales"].Columns["Amount"]` | One column |
| `Model.Tables`, `Model.AllMeasures`, `Model.AllColumns` | Collections used inside approved `foreach` statements |
| `Selected.Tables`, `Selected.Measures`, `Selected.Columns` | The selection captured when Preview starts; an empty matching selection is rejected |

Property assignments require a semicolon. A collection requires `foreach (var name in collection) { ... }`; the body may edit only that loop's current object. Nested loops, filters, other variable declarations, conditional statements and cross-target statements inside a loop are rejected.

| Object | Writable properties |
| --- | --- |
| Table | `Name`, `Description`, `IsHidden` |
| Column | `Name`, `Description`, `IsHidden`, `DisplayFolder`, `FormatString`, `SummarizeBy`; `Expression` for calculated columns |
| Measure | `Name`, `Description`, `IsHidden`, `DisplayFolder`, `Expression`, `FormatString`, `FormatStringExpression` |

Values can contain regular quoted strings, verbatim `@"..."` strings, Boolean literals, an approved `AggregateFunction` member, and concatenations with `loopObject.Name` or `loopObject.Table.Name`. A table has no containing table, so `table.Table.Name` is rejected. Quoted-string escapes supported by this subset are `\n`, `\r`, `\t`, `\\`, `\"` and `\0`; verbatim strings support doubled quotes. Line and block comments are accepted. Property-specific validation still applies: for example, `IsHidden` must evaluate to `true` or `false`.

Approved aggregation names are `None`, `Sum`, `Count`, `DistinctCount`, `Min`, `Max`, `Average` and `Default`. `table.AddMeasure(name, expression, optionalFolder)` creates a measure, and `measure.Delete()` deletes a measure after checking DAX callers. Creation/deletion of other object types remains outside this subset. Referenced measures must have their callers updated or removed first.

```csharp
foreach (var m in Selected.Measures)
{
    m.DisplayFolder = "Finance";
    m.Description = "Measure: " + m.Name;
}

Model.Tables["Sales"].AddMeasure(
    "Quantity Total", "SUM('Sales'[Quantity])", "Totals");

foreach (var c in Selected.Columns)
{
    c.SummarizeBy = AggregateFunction.None;
}
```

Unsupported source is rejected in full, including an unsupported statement after otherwise valid edits. Examples include `using`, `#r`, interpolation, arithmetic, arbitrary calls, reflection, LINQ, file/process/network access, and unbounded loops. No accepted prefix is applied after a parse failure.

Limits: 256 KiB source characters, 20,000 tokens, 2,000 typed statements, 100 parts per value, 256 KiB per literal or expanded value, 512-character object names, 64 MiB serialized metadata characters, and 20,000 expanded object operations. These limits bound the interpreter; they do not promise constant-time TOM serialization or DAX dependency analysis.

## Detached preview and native apply

1. The UI thread captures selected identities, native object bindings, an immutable copy of the recipe, complete serialized model metadata, and its fingerprint.
2. The shared background queue deserializes the captured JSON into a detached public TOM `Database`. It interprets the typed operations and computes before/after property rows on that detached object graph. It never creates a second `TabularModelHandler`, because the inherited TE2 runtime contains process-global model state.
3. The UI thread materializes the exact diff into the shared `AuthoringPreview`. A changed session or whole-model fingerprint invalidates the result. Changing the script draft also invalidates the visible preview.
4. The user reviews the object/property rows and explicitly applies them. The shared preview rechecks staleness and performs a single native undo transaction with postcondition validation and rollback on failure. Apply does not deploy or save a remote model.

Cancellation is cooperative during queued computation and object/DAX loops. Initial UI-thread metadata serialization and native apply are synchronous. The `ComputeAsync` helpers are designed to run inside `BackgroundTaskQueue`; they do not independently create a worker thread.

Rename previews include original DAX reference updates in measures, calculated columns/tables, calculation items and their formats, UDFs, RLS expressions and detail-row expressions where those references can be resolved. Strings and comments are preserved. Native automatic DAX fixup is suppressed only around the reviewed rename; explicit caller rows apply in the same transaction. Potential inferred calculated-table source-column renames are rejected and directed to the native editor.

Static/dynamic measure format transitions expose the resulting format changes and the native `Format` annotation change. The annotation is ordered explicitly so one Undo restores it correctly. Dynamic formats require compatibility level 1601; a script never upgrades compatibility. DAX validation is structural and metadata-aware; the connected engine remains authoritative for complete DAX type and execution validity.

## Trusted Legacy

Trusted Legacy invokes the existing TE2 public `ScriptEngine.CompileScript` and native model wrappers. This mode is unrestricted C#, and is never invoked by Safe Preview. The UI displays a persistent trust warning and requires an explicit checkbox acknowledgment. Editing or loading trusted source, or changing model sessions, resets that acknowledgment.

Before execution, PbiBench captures the current source, selection and model fingerprint, and writes a unique local BIM recovery snapshot. Run rejects missing snapshots, changed sessions/models, consumed tickets, disabled undo and unfinished native transactions. A successful preparation does not itself run code.

Compiler diagnostics, runtime errors and bounded `Console.Out`/`Console.Error` output are shown in the result. Console writers are restored in `finally`, including compiler/runtime failure paths. The existing native transaction supports one Undo for ordinary wrapper changes; runtime failure attempts native model rollback. The snapshot remains available for manual recovery.

This is not a security sandbox. File, network and process effects cannot be previewed, rolled back or canceled reliably. Existing scripts can alter global/native state or bypass wrapper undo, so model Undo cannot be guaranteed for arbitrary trusted source. Native compilation/execution runs on the UI thread and cannot be forcibly canceled; a long or nonterminating trusted script can block the application. This pass does not change TE2 hosting to pretend otherwise. Script sources are limited to 1 MiB characters for preparation, and captured console text to 256 KiB characters.

Snapshots are model metadata artifacts under the active profile's `TrustedScriptSnapshots` directory; they can contain connection metadata and should be handled like the original model file. The normal profile is `%LOCALAPPDATA%\PbiBench`. An injected `settingsDirectory` also redirects snapshots and macros, so isolated acceptance profiles never load real user macros.

## Action Recorder and macros

The recorder takes explicit start/stop checkpoints around model editing. It follows wrapper object identity across renames and compares the net metadata change. It records supported table/column/measure properties, names, folders, measure expressions, measure creation and measure deletion. It does not serialize clicks or keyboard gestures. Changes undone before Stop disappear from the net result; creation followed by deletion likewise has no final object to reproduce.

The generated recipe orders parent table renames before child edits and deletions after caller changes. Replay always uses the same detached preview and reviewed apply path. It therefore validates the current model rather than assuming a recorded operation is still valid. Complex rename collisions or dependencies may require a native editing operation and a fresh recording.

Unsupported metadata changes, including other object creation/deletion and roles, relationships, cultures or connection properties, produce explicit notices. They are not included in the recipe. Exported recipes contain the supported typed operations, not a full model patch or those unsupported changes. A recipe should not be treated as a complete record when Stop reports such notices. Optional generated compatibility C# is not part of this pass; typed recipes are the executable recording format.

Macro entries have an explicit `SafeScript`, `Recipe` or `TrustedLegacy` mode. Loading/importing an entry never executes it and never grants trusted acknowledgment. Recipe macros contain validated typed operations; script macros cannot smuggle an additional hidden recipe. Libraries support load/save, import/export, selection and removal. The active profile stores `macros.json` with atomic replacement, preserving the prior file if a canceled write fails before commit.

Recipe/library JSON uses version 1, a maximum nesting depth of 20 and a 4 MiB file limit. Libraries contain at most 256 macros, each with a GUID identity, name up to 128 characters and source up to 256 KiB characters. Unknown operation/target enum values, missing required recipe values and inconsistent operation fields are rejected.

## Public interfaces and provenance

The public Microsoft TOM [SerializeDatabase](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.jsonserializer.serializedatabase?view=analysisservices-dotnet) and [DeserializeDatabase](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.jsonserializer.deserializedatabase?view=analysisservices-dotnet) APIs provide metadata serialization and detached database objects. The implementation uses the repository's existing pinned TOM assemblies; documentation version labels do not change those dependencies.

The open-source TE2 [Advanced Scripting documentation](https://docs.tabulareditor.com/te2/Advanced-Scripting) describes its C# scripting and wrapper model. Trusted mode uses the existing pinned MIT source in `vendor/TabularEditor2-2.28.0/TabularEditor/Scripting/ScriptEngine.cs` and the native wrapper/undo path. The safe parser, recipe schema, detached interpreter, diff, recorder and PbiBench UI are original implementations. No proprietary TE3 code or UI assets are used.

## Focused evidence

The V9.4 focused checks completed on 2026-09-05:

| Suite | Result | Coverage |
| --- | --- | --- |
| `SafeScriptTests` | 17 passed on net10.0 and 17 on net48 | Accepted grammar, comments/strings, unsupported and malicious constructs, source/expansion bounds, missing required values, recipe/macro round trips and canceled-write preservation |
| `ScriptPreviewTests` | 10 passed on net48 | Detached no-mutation preview, worker computation, stale plans, empty selection, DAX rename callers, format/annotation side effects, create/delete guards, recorder replay and native one-Undo restoration |
| `TrustedScriptBoundaryTests` | 2 passed on net48 | Actual initialized TE2 compiler, trust rejection, snapshot creation, successful mutation and one Undo, stale/consumed tickets, compiler/runtime diagnostics, runtime rollback and Console restoration |

Trusted boundary tests run on bounded STA background threads in the nonparallel `Native TE2` xUnit collection, shared with model-editor boundary tests, to respect inherited process-global state. Scripts in these tests perform benign model edits and console writes; they do not execute file, network or process effects. TRX files are in `artifacts/v94-script-tests`. The root V9.4 gate records the complete application build, broader regression suites and launch/capture results separately.

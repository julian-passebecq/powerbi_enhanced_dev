# TE2 Foundation Plan

## Decision

Use TE2 as the **semantic engine ancestry** of PbiBench.

This is stronger than merely studying it.

## What is directly valuable

The supplied TE2 snapshot contains:
- main WinForms application targeting .NET Framework 4.8
- `TOMWrapper`
- `BPALib`
- ANTLR grammar project
- tests
- scripting/editor infrastructure
- best-practice analyzer
- serialization helpers
- object manipulation abstractions.

The snapshot's `BPALib.csproj` already multi-targets:
- .NET Framework 4.7.2
- .NET 8
- .NET 9

That makes BPA one of the easiest components to adopt into modern PbiBench first.

`TOMWrapper` and the main app remain .NET Framework 4.8 in the supplied snapshot, so they need deliberate modernization.

## Do not start by porting every file

### Stage A — frozen upstream

```text
vendor/
  TabularEditor2/
```

Keep the supplied source intact.

Add:
- upstream commit/version note if discoverable
- license
- third-party license inventory
- baseline build notes.

### Stage B — compatibility tests

Create tests for:
- load model
- object tree enumeration
- measure create/update
- relationship change
- dependency lookup
- BPA evaluation
- serialization
- undo/redo behavior
- script helper semantics that PbiBench wants to preserve.

### Stage C — modern semantic engine

Target:

```text
PbiBench.SemanticModel
PbiBench.BestPractices
PbiBench.Automation
```

Port/wrap the minimum required TE2-derived code.

Prefer current Microsoft TOM NuGet APIs rather than carrying compatibility shims forever.

### Stage D — new UI

The new WPF shell talks to PbiBench services.

Do not have WPF view models manipulate TE2 WinForms controls.

### Stage E — legacy script bridge

Implement a compatibility context for trusted TE-style scripts:

```text
Model
Selected.Tables
Selected.Columns
Selected.Measures
Info(...)
Warning(...)
Error(...)
Output(...)
```

Then add:
- import MacroActions.json
- preview affected objects
- trusted/untrusted classification
- optional conversion of common scripts to typed Actions.

## What not to reuse automatically

TE2 includes third-party components with separate license files.

Before copying:
- editor controls
- tree controls
- wildcard libraries
- installer code

review their individual licenses.

PbiBench does not need to inherit old UI dependencies merely because TE2 did.

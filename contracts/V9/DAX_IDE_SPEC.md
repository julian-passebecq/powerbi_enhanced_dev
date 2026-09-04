# DAX IDE Specification

## Language service

Create `PbiBench.Dax.LanguageService`.

Inputs:
- DAX text
- cursor/selection
- model metadata snapshot
- query/script context
- compatibility level
- UDF catalog

Outputs:
- tokens/highlighting
- diagnostics
- completions
- signature help
- hover info
- symbol locations
- references
- code actions.

Use TE2's open parser/grammar foundation where appropriate and license-compatible.
Do not copy TE3 editor implementation.

## Completion

Suggest:
- tables/columns/measures
- variables
- DAX functions
- UDFs
- query keywords
- parameters

Filter UDF completions by context/type where possible.
Invalid UDF definitions should not be suggested.

## Navigation

- F12 Go to Definition
- Alt+F12 Peek Definition
- references
- Back / Forward navigation

Support:
- measures
- columns
- tables
- UDFs
- variables within document
- calculation items when resolvable.

## DAX Query documents

Features:
- multiple tabs
- `.dax` / `.msdax`
- execute all
- execute selection
- execute current statement
- multiple EVALUATE result tabs
- row count
- elapsed time
- export CSV
- copy
- query history
- PBIP DAXQueries folder integration.

## Code actions

Start with correctness-preserving or previewable actions:
- Format
- Rename local variable
- Extract expression to variable
- Inline variable
- Go/Peek definition
- Define measure in query
- Define UDF
- Define UDF with dependencies
- Inline UDF
- qualify/unqualify model references where unambiguous
- model-wide rename through semantic object model

Performance-oriented actions are suggestion + benchmark, never blind auto-fix.

## DAX Scripts

PbiBench multi-object DAX document:
- serialize selected/all DAX objects into editable script
- validate before apply
- show semantic object diff
- partial apply
- undo as one transaction.

## Debug/Explain

Do not promise full TE3 debugger yet.

Build `DAX Explain`:
- dependency tree
- VAR list
- selected subexpression
- generated diagnostic query
- evaluation/filter context input
- intermediate result grid

Later a step debugger can grow from this.

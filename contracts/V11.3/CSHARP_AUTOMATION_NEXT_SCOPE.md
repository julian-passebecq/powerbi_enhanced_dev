# Practical C# automation — next useful functionality

Continue C# where it helps semantic-model automation. Do not build Visual Studio.

## Add now

### Compiler Problems panel + navigation
Use existing authoritative TE2 compile diagnostics.
Show severity/code/line/column/message.
Selecting a diagnostic activates the correct script and moves the caret without execution.

### Macro context / enable rules
Add bounded optional macro metadata:
- allowed selection kinds: Model/Table/Column/Measure;
- min/max selected count;
- connected-model requirement;
- mode: Safe / Recipe / Trusted.

Show disabled macros with reason and block incompatible execution.
Preserve backward compatibility.
Do not use arbitrary executable expressions as enable conditions.

### Selection-aware semantic snippets
Add a small set:
- explicit SUM measures from selected numeric columns;
- hide selected key columns;
- set display folder on selected measures;
- descriptions/format strings;
- format selected DAX;
- COUNTROWS measure.

Prefer Safe Preview where representable. Otherwise generate reviewable Trusted C# text; never auto-run.

## Later, not forbidden
Richer semantic completion, more snippets/macros, optional Roslyn if isolated cleanly, richer navigation/formatting.

Still avoid full project system, C# debugger, NuGet/MSBuild IDE.

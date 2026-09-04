# PbiBench DAX language service

Original, pure managed editor assistance targeting .NET 10 and .NET Framework 4.8.
The service accepts detached model metadata; it never reads or writes the active TOM model.
Public contracts are in `DaxContracts.cs`. Source offsets use .NET UTF-16 indices.

Implemented: source-preserving tokens, structural and qualified model-reference diagnostics,
metadata/local-variable/parameter/function completion, contextual reference-typed UDF arguments,
function signatures, definition/reference locations, navigation history, guarded previewable
text edits, and lexical query statement/selection extraction preserving DEFINE declarations.
Snapshot capture and editor scheduling remain in the existing Semantic/ModelEditor boundary.

The diagnostic engine intentionally does not claim complete DAX type or evaluation-context
validation. Unresolved virtual columns and unfamiliar engine functions are left to execution.
The catalog provides broad offline function completion; common functions and parsed UDFs have
parameter signatures. Functions without curated signatures direct users to the Microsoft reference.
Reference locations cover the analyzed document and its model targets; whole-model caller lists
continue through TE2's existing dependency service.

## Sources

The tokenization, binding, query extraction and UI-independent service implementation are
original PbiBench code. `DaxFunctions.txt` contains function identifiers extracted from the
MIT-licensed `AntlrGrammars/DAXLexer.g4` in pinned Tabular Editor 2.28.0,
commit `75f10e331b8de0dda5c213180b9b8867b4a38191`. Its notice is included in
`TE2_GRAMMAR_LICENSE.txt`. No TE3 source, assets or internal implementation were used.

UDF behavior is based on public Microsoft documentation:
- https://learn.microsoft.com/en-us/dax/function-statement-dax
- https://learn.microsoft.com/en-us/dax/best-practices/dax-user-defined-functions
- https://learn.microsoft.com/en-us/dax/dax-function-reference

Run `dotnet test tests/PbiBench.Dax.LanguageService.Tests/PbiBench.Dax.LanguageService.Tests.csproj`
with the repository SDK. The suite covers both target frameworks.

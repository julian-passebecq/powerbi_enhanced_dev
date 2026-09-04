# PbiBench.ModelEditor

This project is the **TE2++ Model experience inside PbiBench**.

Pass 1 may use a compatibility host around selected TE2 WinForms controls to get a working editor quickly.
Do not copy the full TE2 UI architecture into the rest of PbiBench.

Target boundaries:

- `IModelEditorSession`
- `IModelSelectionService`
- `IPropertyEditorService`
- `IModelCommandService`
- `IBestPracticeService`
- `IScriptHost`
- `IRelationshipDiagramSource`

Codex should design adapters around actual TE2 APIs after the upstream build is understood.
Do not invent fake TE2 types in production code before inspecting the source.

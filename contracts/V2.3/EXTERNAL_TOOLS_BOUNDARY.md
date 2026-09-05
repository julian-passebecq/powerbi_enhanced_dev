# External tools boundary

## Problem to fix

`PbiBench.DaxStudio` currently contains generic:
- process launch adapter;
- Windows argument quoting;
- tool discovery;
- status/version discovery;
- generic CompanionTools catalog/context.

Report Studio therefore references `PbiBench.DaxStudio` just to launch Desktop/VS Code.

This is conceptually wrong.

## Create neutral shared library

Recommended:
`src/PbiBench.ExternalTools/PbiBench.ExternalTools.csproj`

Targets:
`net10.0;net48`

Own:
- `IProcessAdapter`
- `SystemProcessAdapter`
- `ProcessLaunchRequest`
- `WindowsCommandLine`
- `ExternalToolDefinition`
- `ExternalToolStatus`
- generic executable discovery
- configured-path validation
- process launch
- common ToolContext DTO
- PbiBench child-process discovery from component manifest

No UI.
No auth.
No TE2.
No Fabric.
No DAX logic.

## DAX Studio module after refactor

`PbiBench.DaxStudio` owns only:
- DAX Studio executable definition
- DAX Studio query/connection handoff
- DAX-Studio-specific arguments/file preparation
- specialist capability metadata

It depends on `PbiBench.ExternalTools`.

Do not add any new DAX Studio capability in this pass.

## Report Studio after refactor

Report Studio:
- references PbiBench.Pbir
- references PbiBench.ExternalTools
- does NOT reference PbiBench.DaxStudio

It can launch:
- Desktop
- VS Code
- Explorer via OS-specific local action

## Main PbiBench shell

Uses `PbiBench.ExternalTools` for:
- Report Studio
- Fabric Toolbox
- Bravo
- Desktop
- VS Code
- optional Theme Forge browser link

Uses `PbiBench.DaxStudio` only for Analyze-in-DAX-Studio.

## Tests

- project-reference closure matches module catalog
- Windows quote regression tests stay green
- DAX Studio handoff unchanged
- Report Studio Desktop/VS Code launch args unchanged
- child-app package discovery works
- no semantic/Fabric credential object enters ExternalTools

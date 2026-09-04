# TE2 component reuse map

## High priority

### BPALib
The supplied TE2 snapshot already multi-targets modern .NET (`net8.0` / `net9.0` in addition to `net472`).

Action:
- test net9 build,
- add/validate net10 target,
- wrap it behind PbiBench rule APIs,
- preserve upstream license/notices.

### TOMWrapper
The supplied snapshot targets .NET Framework 4.8 and current-ish Analysis Services assemblies.

Action:
- create a port branch,
- move to SDK-style project,
- use modern PackageReference,
- current Microsoft.AnalysisServices package,
- isolate WinForms-specific dialogs/clipboard/UI calls,
- keep data/model logic.

### Tests
Use upstream tests as behavioral specification.
Add parity tests around:
- object creation/deletion
- rename
- relationships
- measures
- calc groups
- annotations
- save/serialize
- dependencies.

## Medium priority

### Scripting
Reuse concepts first, not arbitrary execution.
Create an Action Catalog before a full C# scripting host.

### Connection discovery
Inspect TE2's Power BI Desktop / Analysis Services discovery logic.
Reimplement/port only what is still needed after official Modeling MCP and modern APIs are considered.

### Dependency tree
Port data graph behavior; redesign visualization in WPF.

## Low priority / avoid initial reuse

### Main WinForms UI
Do not reuse as final shell.

### FastColoredTextBox
Replace with a modern editor strategy later (AvalonEdit or WebView2/Monaco only after license/complexity review).

### TreeViewAdv / miscellaneous WinForms controls
Avoid copying unless license and behavior clearly justify it.

### TE2 branding/assets
Do not reuse product branding.

## Upstream update policy

Once PbiBench is working:
- keep TE2 as an upstream remote/submodule or vendored snapshot,
- periodically diff TOM/BPA/connection changes,
- selectively port bug fixes,
- do not merge WPF product UI back into TE2 source tree.

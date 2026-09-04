# TE2 -> modern PbiBench capability matrix

This maps publicly documented feature categories to an independent PbiBench implementation.

| Feature category | TE2 baseline | PbiBench target |
|---|---|---|
| Edit semantic objects/properties | Yes | Preserve + better inspector |
| Syntax highlighting/formula fixup | Yes | richer diagnostics |
| Compare source schema | Yes | Source Schema Inspector |
| Fast metadata editing | Yes | Preserve |
| DAX dependencies | Yes | interactive graph + selection tracking |
| TOM copy/paste/undo/redo | Yes | Preserve |
| Batch changes | Yes | typed previewable actions |
| BPA | Yes | built-in packs + risk-aware fixes |
| C# scripts/macros | Yes | preview sandbox + recorder |
| AI assistant | No | PbiBench Agent/Assistant |
| C# scripts with Preview | No | safe clone/diff preview |
| Built-in BPA rules | No | PbiBench Core Rule Pack |
| Calendar Editor | No | build |
| Semantic Bridge | No | later PbiBench Semantic Compiler |
| Localization | No | app resources later; metadata translations separate |
| Preview table data | No | multi-tab preview |
| Multiple table previews | No | build |
| DAX queries | No | build |
| Partial DAX execution | No | build |
| Pivot/Matrix testing | No | PbiBench Pivot Lab |
| Multi-object DAX script | No | build |
| DAX debugger | No | DAX Explain first; full debugger later |
| DAX code actions | No | build safe code actions |
| VertiPaq Analyzer | No | integrate open analyzer/adapters |
| Optimizer | No | own rules + external licensed adapter |
| Translation/Perspective editors | Raw properties | dedicated editors |
| Direct Lake create/edit | limited/scriptable | guided wizard |
| IntelliSense | limited | PbiBench DAX Language Service |
| Async refresh | No | background task/refresh queue |
| Table Groups | No | app-side virtual groups |
| Import from Fabric | No | Fabric Import Wizard |
| Preview Fabric data | No | build |
| Model diagram | No | existing PbiBench diagram+ |
| Workspace mode | No | Dual-State Workspace |
| DAX package manager | No | optional DAXLib-compatible package client |
| CLI | legacy TE2 CLI | own structured `pbibench` CLI |
| Agentic workflows | scripts only | command/action/MCP architecture |
| Semantic testing | No | assertions/snapshots/A-B tests |

## Important design choice

"Feel like TE3" should mean:
- fast context switching,
- powerful editor,
- specialist panes,
- test/preview workflows,
- background operations,
- very little need to open other tools.

It should **not** mean copying TE3 visual appearance or implementation.

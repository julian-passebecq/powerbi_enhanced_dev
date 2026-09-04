# TE3 capability map — independent PbiBench implementations

This is a product planning document, not an instruction to copy TE3.

Sources are public descriptions/documentation only.

| Modern semantic IDE capability | TE2 base? | PbiBench independent implementation | Target |
|---|---:|---|---:|
| Full TOM property editing | Yes | Modernized TE2 semantic engine | V1/V2 |
| Bulk object changes | Yes | Typed Action engine + script bridge | V2 |
| BPA | Yes | TE2 BPALib + own rules/fixes | V1/V2 |
| C# scripting/macros | Yes | Trusted script host + typed actions | V2 |
| Undo/redo | Yes | Transaction journal + command stack + Git snapshot | V2 |
| Dependency browser | Yes | Object graph + D3 dependency view | V2 |
| Advanced DAX editor | Partial | Original editor with model-aware completion/diagnostics | V2/V3 |
| DAX query tabs | No | Execute DAX through supported XMLA/model interfaces | V2 |
| Execute selected query text | No | Editor selection -> query executor | V2 |
| DAX result tables | No | Virtualized WPF DataGrid | V2 |
| Pivot/matrix testing | No | Original pivot test surface | V3 |
| DAX multi-object scripting | No | Measure Script document + parser + object diff | V3 |
| Code actions/refactors | No | Rename/extract/format/reference refactors | V3 |
| DAX debugger | No | First build DAX Inspector/Explain; true debugger only if public APIs allow | V4+ |
| Table data preview | Limited/No | Paged preview grid | V2 |
| Multiple simultaneous previews | No | Multiple tabs | V3 |
| Model diagrams | No | Original TOM -> D3/WebView2 graph | V2 |
| Relationship editor | Basic | Diagram + property inspector | V2 |
| VertiPaq integration | No | Reuse/adapter to permissive VertiPaq Analyzer/VPAX tooling | V3 |
| Performance tuning | No | Query metrics + model stats + regressions | V3/V4 |
| Advanced/background refresh | No | Refresh task queue with progress/cancel | V4 |
| Workspace live+disk synchronization | No | PbiBench Workspace Mode: PBIP Git tree + optional live model sync | V3 |
| Macro/action recorder | No | Record PbiBench command journal -> typed action/C# scaffold | V4 |
| Perspectives editor | Basic model support | Dedicated tri-state grid/tree | V2 |
| Translations editor | Basic model support | Translation matrix + optional provider adapter | V3 |
| Calendar editor | No | Own calendar/date-table wizard + templates | V2 |
| Direct Lake model support | TE2 limited | TOM/Fabric/PBIP path using current Microsoft APIs | V4 |
| Import/preview Fabric data | No | Fabric adapter | V5 |
| Table groups/workspaces | No | UI-only semantic domains/groups | V3 |
| Semantic migration/bridge concepts | No | Separate model conversion/export adapters using public schemas | V5 |
| AI assistant | No | MCP-orchestrated PbiBench Agent | V4 |
| Git/DevOps workflow | File-oriented | Deep PBIP/TMDL/PBIR Git UX + CLI validation | V2/V3 |

## Key distinction

The goal is **modern semantic engineering parity at the workflow level**, not pixel/API parity with a commercial product.

PbiBench can be better suited to this user's workflow by combining:
- TE2's open engine,
- PBIP/PBIR Git workflows,
- Power BI Modeling MCP,
- DataForge truth tests,
- report authoring,
- VizForge,
- Fabric scenario planning.

TE3 remains a product benchmark, not a source dependency.

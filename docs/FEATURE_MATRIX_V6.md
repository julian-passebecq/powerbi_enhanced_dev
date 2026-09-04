# Feature Matrix — TE2 base, PbiBench additions, external specialist

| Capability | TE2 base | PbiBench plan | DAX Studio / external |
|---|---|---|---|
| Edit TOM objects/properties | Yes | Preserve/modernize | |
| Measures/calculated objects | Yes | Preserve + improved editor | |
| Relationships | Yes | Preserve + diagram | |
| Calculation groups | Yes | Preserve + action templates | |
| UDF awareness | TE2 2.27+ | First-class editor/library in Pass 2 | |
| Calendar objects | TE2 2.27+ | Wizard/editor Pass 2 | |
| Perspectives | Yes | Better grid/editor Pass 2 | |
| Translations | Yes | Better matrix/editor Pass 2 | |
| BPA | Yes | Better UX + safe fixes | |
| C# scripts/macros | Yes | Trusted bridge + typed actions | |
| Bulk changes | Yes | Typed action engine | |
| Dependency browser | Yes | Improve + D3 view | |
| Undo/redo | Yes | Preserve + action journal | |
| DAX formatting | Basic/external patterns | Integrate in Pass 1 | |
| Modern DAX editor | Limited | Build | |
| DAX query tabs/results | No | Build light in Pass 2 | Deep analysis external |
| Server Timings | No | route/attach results later | **DAX Studio** |
| Query plans | No | route/attach later | **DAX Studio** |
| VPAX | No | adapter later | DAX Studio / VertiPaq tools |
| Model diagram | No | **Build Pass 1** | |
| Data preview | Limited/not TE2 feature | Build Pass 2 | |
| Pivot/matrix testing | No | Later | |
| DAX script multi-object doc | No | Build Pass 2/3 | |
| Code actions/refactors | No | Build progressively | |
| Full DAX debugger | No | Do not promise initially | DAX Studio aids diagnosis; TE3 remains benchmark |
| PBIP/TMDL workspace | Partial serialization/TMDL support | **First-class** | VS Code optional |
| Git semantic diff | File-oriented | **Build** | Git CLI |
| PBIR report engineering | No | **Build Pass 3** | Power BI Desktop renderer |
| Visual calculations | No | Build Pass 3 | |
| Fabric REST management | No | Build Pass 4 | |
| XMLA/TOM cloud management | TE2 connection basis | Extend/control | |
| Agent/MCP | No | Build Pass 5 | Microsoft MCP external |
| DataForge truth QA | No | Build Pass 5 | |
| VizForge | No | Build Pass 5 | Node/pbiviz toolchain |

## Product positioning

PbiBench should aim for the TE3-like productivity features that are valuable to this user's workflow,
but it must not block progress on commercial-feature parity.

Prioritize:
1. Model workflow speed
2. Repeatable automation
3. DAX correctness/testing
4. Git/PBIP
5. report/Fabric workflow
6. safe AI

over:
- pixel parity,
- reproducing every TE3 convenience,
- a full custom DAX debugger.

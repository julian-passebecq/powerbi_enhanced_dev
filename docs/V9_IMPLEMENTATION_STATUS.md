# V9 implementation status

V7 prerequisite: **functional/structural gate complete**, recorded in `V7_FUNCTIONAL_GATE.md`. Preserved portable build: `artifacts/PbiBench-V7-20260905/`; its independent packaging launch check also passed all 15 checks. Taskbar icon appearance and 100/125/150% pixel-level DPI appearance remain manual visual QA pending and do not block implementation, per the user.

| Milestone | Status |
|---|---|
| V9.1 DAX IDE Core | In implementation and verification |
| V9.2 Data Exploration | Not started |
| V9.3 Model Authoring Pro | Not started |
| V9.4 Automation / QA / Optimization | Not started |
| V9.5 Fabric / Refresh / Workspace | Not started |
| V9.6 CLI / Agent / Compiler | Not started |

Milestones are implemented in this order. The final `contracts/V9/ACCEPTANCE_GATE_V9.md` remains required.

## V9.1 boundaries and sources

The new DAX language service is original code over immutable metadata snapshots. The MIT TE2 grammar supplies open built-in function identifiers with its license preserved; tolerant source-span tokenization handles current documented UDF syntax independently. Original diagnostics are editor assistance, not a replacement for Microsoft engine validation.

Query execution owns a separate TOM session and does not reuse or cancel the hosted model editor connection. Display limits bound retained result rows and cells, not engine work. Query files use the semantic model's `DAXQueries` folder when that project context is unambiguous. DAX Studio remains a standalone deep-analysis tool.

Public behavior/API references:

- [Microsoft DAX queries](https://learn.microsoft.com/en-us/dax/dax-queries): DEFINE scope, multiple EVALUATE statements, and ordering clauses.
- [Microsoft FUNCTION statement](https://learn.microsoft.com/en-us/dax/function-statement-dax): UDF parameter types, passing modes, defaults, and lambda body syntax.
- [Microsoft DAX query view](https://learn.microsoft.com/en-us/power-bi/transform-model/dax-query-view): query files under the semantic model/report DAXQueries folder.
- [Microsoft AmoDataReader.NextResult](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.amodatareader.nextresult?view=analysisservices-dotnet): multiple rowsets through the public reader.
- [Microsoft Server.CancelCommand](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.core.server.cancelcommand?view=analysisservices-dotnet): cancellation of the independent query session.

No proprietary TE3 source, assets, or decompilation are used.

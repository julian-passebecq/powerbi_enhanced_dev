# Dependency update matrix

| Dependency / upstream | Current use | Owning lane | Allowed coupling | Update trigger | Mandatory regression |
|---|---|---|---|---|---|
| Tabular Editor 2 / TOMWrapper | semantic editor, undo, trusted scripting | TE2 | Semantic IDE/ModelEditor only | intentional TE2 upgrade | native edit/undo/script/model serialization suite |
| Microsoft.AnalysisServices/TOM/XMLA | model/query/refresh/deploy | Semantic + shared | behind Semantic/Core services | SDK/API need | query, serialization, refresh/deploy contract tests |
| Microsoft.Identity.Client | Fabric auth | Fabric | `PbiBench.Fabric` / Toolbox | security/API update | auth flow contract tests, no secret persistence |
| Microsoft.Data.SqlClient | Fabric SQL preview | Fabric | `PbiBench.Fabric` / Toolbox | security/compat update | SQL connection/cancel/redaction tests |
| TE2 DAX grammar identifiers | built-in DAX catalog seed | DAX language | data/license adapter only | DAX/TE2 syntax refresh | language tokenizer/completion/signature regressions |
| DAX Studio | external deep analysis | External tools | process/handoff only | detected external update | launch args/connection handoff smoke |
| Power BI Desktop | render/author/live local AS | External/PowerBI | process/project/live endpoint integration only | external version changes | discovery/handoff/live endpoint smoke later |
| Git CLI/API | PBIP/Git workspace | Git | Git adapter | Git compatibility issue | read/diff/path-safety tests |
| OpenAI Responses API | optional existing provider | Optional integration | no longer core; local export preferred | only if optional provider maintained | provider boundary tests, never required for base app |
| Databricks Metric View docs | bounded compiler prototype | Prototype | no runtime package | explicit prototype work only | prototype parser/guard tests |
| SQLBI VPAX fixture | test data for VPAX handling | Test fixture | tests only, preserve notice | fixture replacement | attribution/license + parser tests |


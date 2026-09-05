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


## V11.1 implementation pins and lanes

- TE2 remains 2.28.0 / `75f10e331b8de0dda5c213180b9b8867b4a38191`; existing patches unchanged.
- C# assistance: original `PbiBench.CSharp.LanguageService` 11.1.0; no Roslyn dependency. UI reuses existing FCTB 2.16.24. Its LGPL-3.0 notice/source provenance stays in the upstream packaging lane. Protect with portable V11 language tests and native/WPF scripting checks.
- Export: original `PbiBench.AI.ContextExport` 11.1.0, version-1 ZIP/schema, using Core's existing System.Text.Json 9.0.9 on net48 and framework JSON on net10. Protect scope/redaction/bounds/cancellation/checksums and native capture.
- Toolbox: original net10.0-windows application 0.1.0, using unchanged Microsoft.Identity.Client 4.84.2 and Microsoft.Data.SqlClient 6.1.6 (MIT verified from local package nuspec). It ships in `fabric-toolbox/` with its own dependency/runtime manifests.
- Runtime feature pins are embedded from `provenance.json`. Existing `docs/TE2_NUGET_LICENSE_INVENTORY.json` and extracted package notices remain the dependency-level license inventory; TE2's MIT grant does not replace those notices.

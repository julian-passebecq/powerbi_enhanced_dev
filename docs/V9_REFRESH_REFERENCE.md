# Advanced refresh implementation reference

PbiBench generates a typed TMSL refresh inside one `sequence` with explicit maximum parallelism. The preview lists the database ID, selected tables/partitions, processing effects, and exact JSON. TMSL references use the existing database name, matching the public TOM scripter; connection lookup and preflight also verify the distinct stable database ID. Execution consumes a single-use `ApprovedChangePlan` tied to that exact command and model fingerprint. Changing metadata, routing, options, or approval requires a new preview.

The supported processing types are Full, ClearValues, Calculate, DataOnly, Automatic, Add, and Defragment. Add is restricted to explicit regular M/native-query partitions; Defragment targets a table/model. Import, DirectQuery, and Direct Lake have distinct validation and explanatory messages. For Direct Lake, this workflow exposes Full/Automatic/Calculate and explains framing; it does not claim that refresh imports a complete copy. Unsupported combinations remain blocked.

Power BI/Fabric models can explicitly apply/bypass incremental refresh policy and supply a calendar effective date. Policy application may create, merge, or remove historical partitions; bypassing it can reload all existing partitions. Date overrides require an applicable policy and a processing type that reads policy-managed data. PbiBench does not silently inject multi-step ClearValues/DataOnly sequences.

Development profiles contain versioned typed options and scopes. Optional out-of-line source overrides retain an existing Import partition's source kind (M→M or native query→native query) and existing data-source binding. No arbitrary TMSL, source-type conversion, datasource credential override, or implicit source metadata edit is accepted. Existing credentials, gateway, and privacy bindings must support the reviewed query. Authored source code is included in exported profiles and TMSL; transport connection strings and authentication tokens are excluded. Refresh-loaded development data persists until replaced by another refresh.

Execution uses a newly created TOM server connection with the reviewed target forced into its connection string. It captures current target metadata again before sending the command. It never calls SaveChanges or CancelCommand on the integrated editor's connection. Cancellation targets only that private operation, and progress reports phases rather than inventing row counts or percentages. An engine error is distinguished from a connection loss or cancellation after submission, for which the remote outcome can remain unconfirmed. Server success after a late cancellation is reported as success with a warning. No local Undo or guaranteed remote rollback is promised.

The shell marks the matching editor context for reconnect after a completed remote attempt that could have changed live state; ordinary native saving through the reviewed write gateway is blocked until that context is reloaded. Offline models can preview/export but cannot execute. Unsaved editor metadata must be saved or discarded before execution. Profiles preserve missing/mixed scopes so validation reports them instead of silently reducing the requested operation.

Verification uses deterministic planner tests, private-session fixtures for approval/staleness/cancellation/error paths, detached TOM metadata capture, and STA UI tests. These tests do not represent an authenticated remote refresh. Actual gateway, capacity, permission, provider, and endpoint processing behavior requires a user-authorized connected model.

## Public behavior sources

- [Microsoft TMSL refresh command](https://learn.microsoft.com/en-us/analysis-services/tmsl/refresh-command-tmsl?view=sql-analysis-services-2025) — processing types/scopes, policy parameters, and out-of-line override structure.
- [Microsoft TMSL sequence command](https://learn.microsoft.com/en-us/analysis-services/tmsl/sequence-command-tmsl?view=sql-analysis-services-2025) — explicit maximum parallelism and sequence semantics.
- [Microsoft incremental refresh with XMLA](https://learn.microsoft.com/en-us/power-bi/connect-data/incremental-refresh-xmla) — bypassing policy and effective-date behavior.
- [Microsoft Direct Lake overview](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-how-it-works) — framing and Delta metadata behavior.
- [Microsoft Power BI: Using Out-Of-Line Bindings](https://community.fabric.microsoft.com/t5/Power-BI-Updates-Blog/forward/ba-p/5175510) — source-kind and existing credential/privacy binding constraints.

This is an original PbiBench implementation over public Microsoft interfaces; no proprietary TE3 code or assets are used.

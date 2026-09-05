# Fabric Toolbox V0.2

Launch **Apps / Tools → Fabric Toolbox**. In Settings, explicitly authorize the registered public client, then load a workspace and its items. Search matches name/type without case sensitivity; the type selector narrows the loaded inventory. Select a row to inspect its name, type, workspace/item IDs and job-history support. Copy stable IDs or export the currently filtered inventory to JSON/CSV. Selection handoffs to Semantic IDE remain available.

**Operations** reads recent item job instances for Notebook, DataPipeline and SparkJobDefinition. Select an inventory item, open Operations and press **Refresh jobs**. Filter the returned rows by status or text; select a row for timestamps, duration, failure summary and instance/correlation identifiers. **Show item in inventory** returns to that item's row. Selecting or changing pages never submits jobs. Unsupported types remain visible and return an explicit explanation without an HTTP request. Service permission/availability errors remain errors, not empty successful histories.

## Public API basis — verified 2026-09-05

The adapter uses only `GET /v1/workspaces/{workspaceId}/items/{itemId}/jobs/instances` and its `continuationToken` query parameter. The public response defines job identity/type/status, UTC timestamps, root activity ID and failure reason. Retention is service-limited and varies by item. This is item job history, not a tenant-wide audit feed or a list of all Fabric REST long-running operations. [Microsoft: List Item Job Instances](https://learn.microsoft.com/en-us/rest/api/fabric/core/job-scheduler/list-item-job-instances).

The initial item allowlist follows documented job support: [Notebook jobs](https://learn.microsoft.com/en-us/fabric/fundamentals/job-scheduler), [DataPipeline job instances](https://learn.microsoft.com/en-us/rest/api/fabric/datapipeline/background-jobs/list-execute-job-instances), [Spark job definition instance contract](https://learn.microsoft.com/en-us/rest/api/fabric/sparkjobdefinition/background-jobs/run-on-demand-spark-job-definition). The last reference establishes the shared job-instance resource; the Toolbox implements no POST endpoint. New types can be added in the Fabric lane after verifying their public contracts.

The listing requires delegated item read access (generic Item.Read.All or applicable item-specific read scope). Existing MSAL resource consent uses the registered public client's configured permissions; this pass does not change tenant consent or add write scopes. [Microsoft API scope requirements](https://learn.microsoft.com/en-us/rest/api/fabric/core/job-scheduler/list-item-job-instances).

## Bounds and data handling

- Default job read: maximum 10 pages / 1,000 instances, 2 MiB per response and a two-minute overall deadline. The service contract allows at most 20 pages / 5,000 rows. Reaching the page/row limit explicitly labels the returned history partial. Rows are sorted by start time within that returned subset.
- Pagination reconstructs a URL from the fixed Microsoft API origin and encoded token. Server-provided continuation URLs are never followed. Repeated tokens, duplicate/wrong-item jobs, invalid shapes and oversized responses fail explicitly. Existing bounded 429 retries and cancellation are reused.
- UTC timestamp parsing accepts the API's UTC strings with or without a timezone suffix. End minus start supplies completed duration; active elapsed duration uses capture time. Missing timestamps stay missing. Unknown future job statuses remain visible.
- Public failure details are limited to error code/message (512/2,048 characters). Raw error bodies, arbitrary failure fields and auth headers are not retained in job DTOs or logs.
- Existing workspace inventory traversal remains bounded at 100 pages / 10,000 items. The global Cancel current request control is available from every page. Changing workspace clears prior item/source/job state; controls prevent conflicting requests.
- Export is capped at 10,000 rows / 4 MiB and writes atomically with cancellation. It serializes only workspaceId, itemId, name and type. SQL endpoints, credentials, auth state and arbitrary transport properties have no export path. CSV escapes quotes and prefixes formula-leading cells. Exported names/identifiers are the selected workspace's metadata.

No live Fabric tenant was needed for development or the offline gate. Test handlers supply synthetic responses and assert GET-only behavior. An authenticated Notebook/DataPipeline/SparkJobDefinition history remains a manual integration check.

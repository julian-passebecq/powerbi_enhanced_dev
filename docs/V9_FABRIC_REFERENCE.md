# V9.5 Fabric browser, schema and source preview

PbiBench uses its existing `PbiBench.Fabric` transport project, now targeting .NET 10 and .NET Framework 4.8. The Windows application continues to host TE2 2.28.0. The Fabric page creates no network requests until a product action requests sign-in, browsing, or preview. Import, schema updates and storage-mode conversion use the shared native `AuthoringPreview` review/undo mechanism; they do not save or refresh the remote model.

## Sign-in configuration

Use an organization's own Entra public-client registration. Enter the tenant GUID and client/application GUID in the Fabric page. Register `http://localhost` as a mobile/desktop redirect URI. PbiBench uses MSAL's system browser, with a cancellable five-minute sign-in deadline. No password, client secret, token, or refresh token is written to the workspace. The token cache is held in memory and cleared on sign-out; browser/organization single-sign-on state remains controlled by Entra.

Configure delegated consent appropriate to the intended read operations. Fabric catalog access needs workspace and item read scopes (for example Workspace.Read.All and Item.Read.All); OneLake table metadata uses Azure Storage's `https://storage.azure.com/` audience, and SQL source preview uses Azure SQL's `https://database.windows.net/` audience. Buttons authorize each resource separately. Background operations only acquire tokens silently and report when fresh consent is required. A single Entra account is retained across resource authorizations; sign out before switching accounts.

Primary behavior references: [MSAL browser use](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers), [MSAL token acquisition](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/overview), and [OneLake table API authentication](https://learn.microsoft.com/en-us/fabric/onelake/table-apis/table-apis-overview).

## Browser and source identity

Fabric REST enumerates workspaces and supported Lakehouse, Warehouse, SQLDatabase, MirroredDatabase and MirroredWarehouse items. Public item properties resolve SQL server/catalog identities; no token-bearing connection string from a service response is used. SQL Database's explicit serverFqdn/databaseName fields and Lakehouse's sqlEndpointProperties are handled separately.

OneLake mode uses the public Unity Catalog-compatible Delta metadata endpoints. SQL mode is an explicit checkbox and discovers tables/views using fixed parameterized system-catalog SELECT statements. OneLake metadata is not a row query API. SQL views therefore appear through SQL browsing; an unsupported or unprovisioned source returns a capability/access error without inventing metadata.

Captured schemas bind workspace/item IDs, source schema/table, source format, SQL endpoint, object type, location, column names/types/nullability/ordinal/collation through a SHA-256 fingerprint. Partial, empty, duplicate-column, modified or oversized schema captures are rejected. SQL schemas are marked `Format=SQL`: SQL timestamp/rowversion remains a binary source type. OneLake reports its actual format (normally DELTA) and Delta timestamp retains temporal semantics. SQL metadata alone does not prove that a source is Delta-backed.

References: [OneLake Delta operations](https://learn.microsoft.com/en-us/fabric/onelake/table-apis/delta-table-apis-overview), [public Delta request/response examples](https://learn.microsoft.com/en-us/fabric/onelake/table-apis/delta-table-apis-get-started), [Get Lakehouse](https://learn.microsoft.com/en-us/rest/api/fabric/lakehouse/items/get-lakehouse), [Get Warehouse](https://learn.microsoft.com/en-us/rest/api/fabric/warehouse/items/get-warehouse), and [Get SQL Database](https://learn.microsoft.com/en-us/rest/api/fabric/sqldatabase/items/get-sql-database).

## Read limits and cancellation

HTTP catalog responses are capped at 8 MiB/depth 64, 100 pages and 10,000 entries. Pagination follows encoded continuation tokens on a fixed trusted API origin and ignores arbitrary continuation URIs. Repeated tokens or duplicate objects fail with a reload instruction. Source schemas allow at most 4,096 columns and 100 bounded warnings. Rate limiting retries at most five attempts with bounded Retry-After delays.

Source data preview uses a new encrypted SQL connection for each operation with a transient access token, no connection pooling, and no certificate trust bypass. The generated SQL selects only chosen quoted identifiers with `TOP (rowLimit + 1)`. The UI displays the first 100 rows; the service allows at most 1,000 rows, 200 selected columns, 200,000 cells, 16 MiB of captured values and a 120-second maximum deadline. Text/binary cells are clipped to 8,192 characters/bytes and the result is explicitly marked bounded. There is no fabricated paging key, data profile, or total row count. The SQL identity and security context can differ from semantic-model RLS and Direct Lake OneLake security, which is stated beside preview.

Cancellation and deadlines flow through private connections and readers, queued on the existing background task service. Replacing a selected source, signing out or loading another fixture invalidates stale work. Source previews can consume Fabric resources; Direct Lake model previews can additionally load columns into model capacity memory.

## Remote transport boundary

The existing Fabric definition-update entry point now requires an `ApprovedChangePlan` whose target and operation bind to `DefinitionUpdateFingerprint` of the exact serialized payload and updateMetadata option. A submitted plan cannot be reused in that service instance. The import wizard does not call this remote entry point.

Long-running operations accept only the public Fabric HTTPS operation paths and retain the same operation ID throughout polling. They inspect Running/NotStarted/Succeeded/Failed/Canceled states, fetch result resources when required, and never send bearer tokens to an arbitrary Location header. Production HTTP clients disable automatic redirects. Error bodies and provider exception details are not exposed as logs or artifacts. Reference: [Fabric long-running operations](https://learn.microsoft.com/en-us/rest/api/fabric/articles/long-running-operation).

## Dependencies and evidence

Microsoft.Data.SqlClient 6.1.6 and Microsoft.Identity.Client 4.84.2 are Microsoft MIT packages. SqlClient 6.1.6 is the patched 6.1 line with TDS bounds and signature-cache fixes; it permits the existing System.Text.Json 9 application line. The necessary MSAL update is explicit and covered by integrated build/launch verification. SqlClient 7.0.2 was evaluated but requires a different major JSON runtime. The TE2 source and its licenses remain unchanged. See [SqlClient 6.1.6 release](https://github.com/dotnet/SqlClient/releases/tag/v6.1.6).

Executable tests use explicitly synthetic HTTP and SQL fixtures to verify public response shapes, typed schema capture, encoding, paging, origin/audience isolation, operation state and identity, approval binding, cancellation, encrypted connection configuration and preview limits. They do not demonstrate successful access to a live Fabric tenant. Live sign-in, source data access and deployment are dependent on the user's configured app, consent and Fabric resource permissions, and are initiated through product actions.

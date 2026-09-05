# PbiBench 2.2.0 — V2 Pass 2

Started from fetched `main`, `1e02628f7b35af0e5b92c0452f86d3b102562cc2`. The complete 11-file request package is retained under `contracts/V2.2/`. Pass 1 remains the foundation: Semantic IDE is net48 with the existing pinned TE2 2.28.0 runtime; Report Studio and Fabric Toolbox are separate .NET 10 WPF processes. No dependency versions or upstream sources were updated.

## Audit corrections

- `pbibench-fast.yml` runs portable V11 and V2 tests, both modern process builds, Toolbox offline WPF tests and both output-isolation checks.
- `pbibench-release-windows.yml` runs V11 on net10/net48, V2 Release, both modern Release builds, Toolbox WPF tests, Report Studio offline WPF smoke and output isolation. It uploads TRX and Report Studio smoke evidence. These jobs do not claim live TE2/Desktop/XMLA/Fabric coverage.
- The local TMDL catalog now reads relative indentation blocks, quoted/escaped names, nested metadata and multiple tables. Unsupported layouts, missing referenced tables and duplicate members yield partial catalogs. Tests cover 1/2/4 spaces, tabs and incomplete inventories. `SemanticCatalogSnapshot` v1 contains field identities, completeness and capture time only. Explicit imports cannot establish model identity/freshness, so absence in an imported snapshot stays unverified.
- Gallery implementation origin, optional reference URL/pin, license, verification and execution mode are separate fields. Native BPA/Profile/format/measure-table routes have no borrowed script implementation attribution.
- Removed `future-report-tooling`; the implemented report features resolve to `pbir`, `report-studio` and `lineage`. The genuinely future Knowledge feature belongs to the shell. Updated generated Feature Map and product version.
- `V2_PASS1_VERIFICATION.md` now links the two successful historical hosted runs and explicitly records that they did not exercise V2.
- `schemas/pbir-write-policy.json` explicitly binds tested definition version/schema pairs and metadata version/schema to the unchanged Microsoft schema bundle commit. The current supported lane is definition 4.0 with definitionProperties 1.0.0/2.0.0 and versionMetadata 1.0.0 containing version 2.0.0. All definition files must also pass pinned offline schema validation. Neither numeric >=4 nor an unknown future 4.x enables writes. Extend the policy only with pinned schemas and regression evidence.

## Report Studio

Open a PBIP, definition.pbir or project directory. Multiple candidates produce an explicit chooser. Search matches page names/IDs, visual type/title/ID and qualified semantic references. The page selector, tree, wireframe, inspector and lineage selection are synchronized. Zoom supports in/out, 100% and fit. One `ReportViewSnapshot` caches lineage for each immutable index/catalog. Schema, broken/unverified reference and hidden badges appear in the tree/wireframe. Desktop, VS Code and Explorer handoffs remain external; configurable executables are discovered by the existing replaceable process adapter.

The Actions gallery retains configure → immutable plan → exact files/diff → explicit review → durable backup → apply → validate → separate restore. The Visual selection tab supports Ctrl/Shift selection for bounded batches (1–200 distinct visuals). Changing selection/parameters clears approval. New actions:

- Visual copy/duplicate accepts optional X/Y offsets within ±4096; nonzero offsets must keep the result inside the destination page. Existing group/resource/cross-model restrictions remain.
- Batch hidden state and common title text/show edits. Scoped or ambiguous multiple title objects are rejected.
- Bookmark display-name rename preserves IDs. Duplicate creates a new ID and appends its order entry. Only tested bookmark/1.0.0 and bookmarksMetadata/1.0.0 are enabled; other pinned versions can be inspected but this action remains unavailable. Bookmark identity and page/order references are validated.
- Report-wide field mapping shows occurrence/file/page/visual counts before review.
- Display-name extraction and explicit application to matching structured projections use `pbibench-display-names.json` v1. Unmatched/conflicting rows reject the entire plan.
- Table/matrix formatting is **detector/preview only**. Field selectors and conditional-formatting compatibility lack sufficient fixtures for safe copying; no write plan is offered.

Windows atomic replacement now tolerates a bounded transient sharing failure while rechecking the reviewed hash on every retry. Persistent locks still fail with guarded rollback and the durable backup. This does not relax approval or freshness rules.

## Semantic/report impact and display names

Semantic View → Report Usage shows `Used in Reports (N)` with report/page/visual links and occurrence counts. Double-click opens the local usage in Report Studio.

The native model handler's existing cancellable ObjectChanging/ObjectDeleting events now guard table/measure/column rename/delete and expression/data-type/summarization refactors when report context exists. The guard reads fresh report snapshots, shows known usages and incomplete/unknown coverage, and can cancel before the semantic mutation. Undo/recovery never prompts. Source files under TE2 are unchanged. A mismatched or unavailable local model association is explicitly advisory.

The impact dialog can open a usage or export a v1 impact/handoff containing qualified before/after identities, usage locations and source hashes. It grants no write authorization. TOM and PBIR must be previewed/applied/recovered separately; there is no atomic TOM+PBIR transaction and no silent PBIR rewrite.

Ctrl+P offers **Report impact · export semantic catalog** and **Report impact · import display-name annotations**. Import previews `PbiBench.DisplayName` annotations through the native guarded local transaction; conflicts, unmatched targets and bounds fail before apply. The annotation includes version, display name and source report/page/visual, with no model values or credentials. Report Studio never accesses TOM.

## C# Gallery v2

Twenty curated entries: ten Safe Recipes, seven native routes and three advanced TrustedDraft templates. Added bounded annotation, native translation, typed dynamic format, inactive-relationship evidence, disconnected measure selector, time-intelligence calculation-group and comparison-group helpers. Search/category/favorites/recent, mode/risk labels and current selection compatibility are available. Preferences persist IDs only; user macros never enter the Verified catalog.

Advanced templates generate exact source only. Inserting them opens the existing Trusted draft editor and clears both trust acknowledgment and compiled-source state. Generation/insertion cannot run code. Tests compile all three templates and verify unchanged model fingerprints; a separate user decision is required in the existing Trusted workflow. Dynamic format actions use the existing compatibility-1601 typed path and model Undo.

## Fabric report snapshot

Fabric Toolbox → Workspaces → select a Report → **Get report definition** → select a snapshot parent. The explicit read-only POST uses `/v1/workspaces/{workspaceId}/reports/{reportId}/getDefinition`. The existing transport handles 429 and the trusted operation poller handles 202; Location and x-ms-operation-id must agree, and operation requests stay on the same trusted identity. Direct 200 and header-only operation identities are supported. Requests have cancellation, bounded retries and a ten-minute overall deadline.

Only canonical InlineBase64 is decoded. Limits are 2,048 parts, 4 MiB decoded per part, 32 MiB aggregate and 48 MiB response JSON. Paths reject absolute/device paths, dot segments, duplicate normalized paths, alternate streams, reserved names, caches, reparse targets and file/directory collisions. Known public report part roots are allowlisted. Credential-bearing JSON fields/connection strings are rejected. Authentication headers and token cache never enter the manifest or handoff.

All parts are checked before writing to a generated staging directory. Publication moves that directory to a **new destination only**; a destination appearing during retrieval is never merged or overwritten. The v1 manifest stores workspace/report IDs, retrieval time, detected PBIR/PBIR-Legacy format and hashes/sizes. Report Studio receives the local definition path only. PBIR-Legacy can be inspected as raw JSON and remains read-only. No report updateDefinition API is added.

## Public evidence checked for this pass

- [Microsoft Report Get Definition](https://learn.microsoft.com/en-us/rest/api/fabric/report/items/get-report-definition): endpoint, 200/202/429, InlineBase64 and response/operation contracts. Retrieval is a read but Microsoft currently requires report read/write permissions and Report.ReadWrite.All or Item.ReadWrite.All; encrypted sensitivity labels can block it.
- [Microsoft Power BI project report format](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-report): enhanced PBIR version wording, legacy read-only boundary, public schemas, bookmark layout and persisted filters.
- [Pinned Microsoft visual configuration](https://github.com/microsoft/json-schemas/blob/83ce11373faada0d01e76264a5cceb0ba70003e6/fabric/item/report/definition/visualConfiguration/2.2.0/schema.json), [bookmark](https://github.com/microsoft/json-schemas/blob/83ce11373faada0d01e76264a5cceb0ba70003e6/fabric/item/report/definition/bookmark/1.0.0/schema.json) and [bookmark ordering](https://github.com/microsoft/json-schemas/blob/83ce11373faada0d01e76264a5cceb0ba70003e6/fabric/item/report/definition/bookmarksMetadata/1.0.0/schema.json) contracts. Existing license/source hashes remain intact.

Validation and the final local Release evidence are recorded separately in `V2_PASS2_VERIFICATION.md`. Post-push validation belongs to hosted CI and the external reviewer.

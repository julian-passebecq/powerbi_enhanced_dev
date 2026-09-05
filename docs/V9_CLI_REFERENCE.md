# PbiBench semantic CLI — V9.6

The Windows console executable is packaged as `cli/pbibench.exe`. The main graphical application remains `PbiBench.exe`; both use the same existing Core, Semantic, Automation and workspace services. The CLI owns one TE2 handler on one STA thread, with a dispatcher to return asynchronous preview work to that owner. Additional model comparisons use detached public TOM databases. Connected queries, refresh and deployment use private sessions and do not cancel or save a GUI editor connection.

## Commands

| Command | Inputs and behavior |
| --- | --- |
| `inspect` | `--model PATH`: model identity, compatibility, safe object inventory and counts. |
| `list` | `--model PATH [--kind Measure]`: safe object metadata; optional kind filter. |
| `get` | `--model PATH --kind Measure --table Sales --name Revenue [--property Expression]`: explicit objects and allowlisted properties. Repeat `--select '{"kind":"Measure","table":"Sales","name":"Revenue"}'` for multiple objects. |
| `set` | Explicit selections plus `--property Description --value TEXT`: native preview and one undo batch; supports the safe table/column/measure property allowlist. |
| `script` | `--script-file FILE --language SafeCSharp\|Dax`: the existing restricted C# recipe parser or DAX authoring service; preview required. There is no trusted arbitrary C# execution command. |
| `action` | `--action OrganizeMeasures` with explicit selections and optional `--action-options JSON`, or an existing `--recipe-file FILE`: existing Automation Gallery and safe recipe services. |
| `bpa` | `--model PATH [--bpa-profile FILE] [--fail-on Error\|Warning\|Information\|None]`: versioned built-in BPA rules and optional project overrides. |
| `query` | `--server ENDPOINT --database NAME_OR_ID --query-file FILE`: bounded DAX execution with typed cells, all result sets, warnings and truncation flags. `--query TEXT` is also supported. |
| `test` | Connected target plus `--tests FILE`: existing versioned semantic-test artifact, scalar/table/row-count/snapshot/A–B assertions. Empty, truncated, stale or mismatched executions do not pass assertions. |
| `refresh` | Connected target and `--refresh-profile FILE`, or typed refresh options listed below: fresh metadata capture, exact TMSL preview, reviewed private-session execution. |
| `validate` | `--model PATH`: offline TOM/TMDL round-trip, conservative DAX diagnostics and BPA; output explicitly states that engine validation was not run. |
| `diff` | `--model LEFT --against RIGHT`: semantic property differences, using detached models and redacted credential fields. |
| `deploy` | `--model PATH --server ENDPOINT --database EXISTING_NAME_OR_ID`: exact source/target comparison and reviewed deployment to an existing model, with recovery backup and a fresh target recheck. `--resolve-conflicts` is explicit source preference. |

Local input may be a BIM, a compatible JSON model, a TMDL definition folder or a PBIP project. Local mutations require an explicit `--output PATH.bim` to persist from the CLI. The source is read again before apply, and output content is revalidated under an exclusive file handle. A newly created destination uses create-new semantics. Existing output keeps a recovery copy. Writing a TMDL folder belongs to the existing Workspace synchronization experience.

`get` does not expose data-source credentials or partition source expressions. Inspection projects plain safe DTOs, never a live native object. User-authored DAX and returned query data remain model data and appear in explicitly requested outputs.

## Preview and approval across processes

```powershell
& .\cli\pbibench.exe set --model .\model.bim --kind Measure --table Sales --name Revenue --property Description --value 'Revenue after returns' --output .\reviewed.bim --review-out .\review.json --json --non-interactive
```

Read the returned `review.changes`, `review.issues`, `review.targetIdentity` and `review.canApply`. Copy the returned **`review.hash`** only after reviewing those exact changes, then use it in a separate invocation:

```powershell
& .\cli\pbibench.exe apply --review .\review.json --approve '<review.hash from preview>' --json --non-interactive
```

Every apply rebuilds the preview from current files/metadata; a saved file never contains executable closures. The content hash binds request parameters, source/output content and exact changes. For a saved review, the displayed approval token also binds the review nonce, creation time, expiry, request and complete review content. The saved envelope stores this token as `approvalHash`; its nested `review.hash` is the underlying content hash. Use the token displayed by the preview result, rather than substituting that nested content hash.

Saved reviews expire after 30 minutes. The CLI durably claims a saved review before applying; claimed attempts remain consumed even if execution fails or its remote outcome is uncertain. Editing the nonce, timestamps or review text invalidates the original approval. A new review requires a new approval. An in-process GUI preview is also one-use and bound to its exact native owner/fingerprint and undo boundary.

Saved envelopes include the complete user-authored typed request. Treat them as project/model artifacts, rather than shareable redacted reports. Keep authentication in host transport configuration; inline authentication-bearing refresh source overrides are rejected. Source-bound local previews require a freshly loaded handler without unrelated unsaved drafts; the GUI's in-memory preview path can operate on existing drafts under its native fingerprint guard.

Remote apply always requires a saved review. A local invocation may use `--apply --approve HASH` with the same explicit request when the previous preview did not use `--review-out`. Saving a review and applying it are separate invocations. Read commands reject apply/approval options.

## Typed request files

All command parameters can be carried in strict camelCase JSON, avoiding shell quoting for expressions and Unicode. Enum values are names, not integers. Unknown fields, duplicate properties and fields belonging to a different command are rejected. The positional verb must match the JSON kind.

```json
{
  "version": 1,
  "kind": "Action",
  "target": { "modelPath": "C:\\Models\\model.bim" },
  "outputPath": "C:\\Models\\reviewed.bim",
  "recipe": {
    "version": 1,
    "name": "Create a reviewed measure",
    "steps": [
      {
        "target": { "scope": "Table", "name": "Sales", "table": null },
        "operation": "CreateMeasure",
        "property": "",
        "value": { "parts": [{ "kind": "Literal", "text": "Net revenue" }] },
        "expression": { "parts": [{ "kind": "Literal", "text": "[Revenue] - [Returns]" }] },
        "displayFolder": { "parts": [{ "kind": "Literal", "text": "Finance" }] }
      }
    ]
  }
}
```

```powershell
& .\cli\pbibench.exe action --request .\action-request.json --review-out .\action-review.json --json --non-interactive
```

Do not combine `--request` and `--review`, or combine either with hidden field overrides. A local model target and connected target are mutually exclusive except for `deploy`, which needs both. Refresh requires only a connected target. Native metadata operations currently use `--model`; connected read queries and tests use explicit server/database. There is no implicit discovery or fallback to another model.

## Profiles and credentials

`--profile FILE` supplies defaults for direct command arguments. A profile contains only these fields:

```json
{
  "version": 1,
  "modelPath": "C:\\Models\\model.bim",
  "rowLimit": 10000,
  "timeoutSeconds": 60
}
```

For a connected profile, replace `modelPath` with `server` and `database`. An optional `connectionStringEnvironmentVariable` names a process environment variable containing transport authentication; `--connection-env VARIABLE_NAME` overrides that name. No request/profile field accepts a connection-string value. Endpoint fields reject connection-string options. Transport errors and connected review text redact credential assignments. Query limits are 1–1,000,000 rows and 1–3,600 seconds.

Request/review files supply their own complete parameters; a profile used alongside them contributes only the authentication-variable name. Relative model/input/output paths resolve against the current working directory, not against the profile directory.

Approval claims normally live in `%LOCALAPPDATA%\PbiBench\CommandApprovals`. For isolated test or runner profiles, `PBIBENCH_CLI_STATE_DIRECTORY` overrides the state root, and `CommandApprovals` remains its child. The override is validated as a full filesystem path and rejects reparse links. Retain this directory across invocations when replay protection is required.

## Refresh and deployment

Direct refresh options are `--refresh-type Full|ClearValues|Calculate|DataOnly|Automatic|Add|Defragment`, `--scope-table TABLE`, `--partition PARTITION`, `--max-parallelism N`, `--no-policy`, `--effective-date yyyy-MM-dd` and `--timeout SECONDS`. A profile can express multiple scopes and supported typed development source overrides. Refresh profiles cannot be silently combined with direct refresh overrides. The planner checks scope, storage mode, policy and command compatibility before exposing an applicable preview.

The returned remote review shows redacted generated TMSL. Approval binds the exact original command and source/target metadata, including resolved database ID and name. Connected operations use the existing `ApprovedChangePlan` services. Cancellation after submission and transport failures that make commit status uncertain return exit 6; callers must inspect/reconnect before deciding to prepare another write. No automatic reverse operation is promised.

See [V9_REFRESH_REFERENCE.md](V9_REFRESH_REFERENCE.md) and [V9_WORKSPACE_REFERENCE.md](V9_WORKSPACE_REFERENCE.md) for the underlying supported operations and limits.

## Output, exit codes and CI

`--json` writes one UTF-8 JSON result to stdout. Errors also write a short message to stderr. No prompts are used; `--non-interactive` makes CI intent explicit. `--schema` returns the capability catalog, while normal command results contain `version`, `kind`, `status`, `exitCode`, `message`, and optional `data`, `review`, `diagnostics`.

| Exit | Meaning |
| --- | --- |
| 0 | Read succeeded or applicable preview produced; a preview does not mean apply occurred. |
| 2 | Usage, malformed/unsupported input or missing required target. |
| 3 | Approval rejected, stale/expired/replayed review, non-applicable preview or validation/assertion failure. |
| 4 | Execution or filesystem failure. |
| 5 | Cancellation before a known completed remote write. |
| 6 | Remote outcome unknown after submission; never treated as success. |

The repository's `scripts/invoke-cli-command.ps1` handles Windows argument quoting, Unicode, separate stdout/stderr and process deadlines. `scripts/invoke-cli-smoke.ps1` runs independent real executable processes against copied local models and isolated approval state. CI can run read commands (`inspect`, `validate`, `bpa`, `diff`) without an approval. Tests of refresh/deploy transport use explicitly labeled fixtures; no live refresh/deployment is claimed by those tests.

## Shared model-facing tool contract

`CommandSchema.Export(modelFacing: true)` provides closed JSON Schema 2020-12 `inputSchema` definitions for nine read/proposal operations, including bounded strings/arrays and explicit recipe targets. Routing paths, endpoints, credentials, arbitrary scripts, apply and remote writes are absent from model inputs. `CommandSchema.ParseModelRequest` enforces the emitted bounded schema and existing recipe/test validation before the host binds routing. Model test proposals are bounded scalar assertions; the ordinary CLI semantic-test artifact supports the full existing assertion categories.

The Agent experience uses its stricter proposal envelope and the same `SemanticCommandService` Action preview/apply path. Generated proposals never receive approval tokens and never execute automatically. PbiBench remains useful with the offline provider and without an LLM.

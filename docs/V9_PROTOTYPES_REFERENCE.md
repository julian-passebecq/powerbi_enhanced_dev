# V9.6 semantic compiler and local DAX package prototypes

These are bounded, original PbiBench prototypes. They use the existing Core, Semantic and App projects, shared background queue, TE2 model owner and authoring review/Undo transaction. They add no runtime dependency or alternate model host. Full SQL translation and a remote package feed remain later work.

## Semantic compiler

**Model tools → Compiler / packages → Semantic compiler** opens Metric View YAML, compiles an intermediate semantic intent, displays diagnostics and exports the complete IR as JSON. The original YAML is retained in that IR, including unsupported content. Select an existing table explicitly before reviewing the supported measure proposals. Compilation and file reads use the shared background queue; an older result cannot overwrite a newer YAML draft.

`Core.Compiler.MetricViewCompiler.Compile(yaml, name)` returns `SemanticCompilation`, containing the original `SemanticIntent`, diagnostics and `CanProposeMetadata`. `ToJson()` exports the IR. `Semantic.Compiler.SemanticCompilerService.Preview(compilation, targetTable)` reparses the preserved source and builds an existing `AuthoringPreview`. It creates local measure metadata only. It never executes SQL, creates a source connection, imports data, creates partitions or silently changes the model's relationships.

Databricks documents versioned YAML with a source, fields/dimensions, measures and additional semantics such as joins and filters. The prototype was independently implemented from that public behavior description. See the [Databricks Metric View YAML reference](https://docs.databricks.com/aws/en/uc-semantics/metric-views/yaml-reference).

Supported input is deliberately explicit:

- Format version `0.1` or `1.1`; a simple `catalog.schema.table` source name.
- Top-level `version`, `source`, `comment`, `fields` or its `dimensions` synonym, `measures` and flat `joins` intent.
- Block lists with two spaces before `- name`, and four spaces before item properties. Plain scalars, complete single-quoted scalars, JSON-compatible double-quoted scalars, comments, and literal `|` / `|-` blocks are supported.
- Direct column field references, optionally prefixed with `source.`.
- Direct-column `SUM`, `AVG`, `MIN`, `MAX`, and `COUNT(*)` / `COUNT(1)` measure candidates. Numeric column existence and type are checked against the explicitly selected table before review. `AVG` proposes `AVERAGE`; row counts propose `COALESCE(COUNTROWS(table), 0)` to make the empty result explicit.

Joins are represented in the IR with source, condition and cardinality, but they block metadata proposals. Filters, SQL query sources, field-alias lineage, SQL transformations, nested joins, parameters, materialization, DISTINCT, window calculations, aggregate arithmetic and unsupported semantic properties also produce blocking diagnostics. YAML aliases, anchors, tags, flow collections, tabs, duplicate properties and unsupported indentation are rejected. Folded `>` blocks are outside this subset. Display labels are retained in the source; measure names use the explicit `name` property.

The parser is bounded to 1 MiB of text and 10,000 lines. Unsupported input can still be inspected/exported as intent with diagnostics; it cannot silently drop a filter or join and produce an applicable model plan. Existing measure/column name collisions block creation. It does not infer column renames, perform an automatic SQL-to-DAX equivalence proof, or validate source grain, RLS, null/blank/coercion, numeric precision or refresh behavior. Compare representative source and model results before deploying a reviewed proposal.

The original sample is `examples/prototypes/orders.metric-view.yaml`. It needs a model table with numeric `Amount`; its illustrative source is not an actual connection.

## Local DAX package manager

**DAX packages** reads a local folder, displays package ID/version, declared license, dependency pins, overall content hash and every captured UDF body, then offers install/update/removal through the same exact authoring review. Editing the folder invalidates its earlier package capture. Compatibility below 1702 blocks installation; the app does not upgrade the model automatically.

This prototype uses an explicitly PbiBench-owned manifest named `pbibench.package.json`. It does not claim DAXLib feed or manifest compatibility. No feed is contacted. There are no installer hooks, source plugins, C# scripts or executable payloads. Only listed `.dax` UDF bodies are read as metadata; nothing invokes the installed function as part of installation.

The manifest schema is exact and case-sensitive:

```json
{
  "schemaVersion": 1,
  "id": "contoso.math",
  "version": "1.0.0",
  "license": "MIT",
  "description": "Declared package description",
  "dependencies": [],
  "functions": [
    {
      "name": "contoso.math.Double",
      "path": "Double.dax",
      "sha256": "<64 hexadecimal characters for the raw .dax file bytes>",
      "description": "Returns twice the supplied integer.",
      "isHidden": false
    }
  ]
}
```

Each dependency is `{ "id": "publisher.library", "version": "1.2.3", "sha256": "<the installed package content hash>" }`. IDs are lowercase dotted namespaces; function names must fall under their package namespace. Versions are exact `major.minor.patch` values. Version ranges, prereleases, automatic dependency resolution and downgrades are outside this prototype. A changed hash under an already installed identical version is rejected; publish a new version.

Manifest/file reads verify raw UTF-8 bytes, with a 256 KiB manifest, 256 KiB per function, 8 MiB total, 1–512 functions and at most 256 dependencies. An installed lock is limited to 256 packages. Unknown or duplicate JSON properties, malformed required values, duplicate function names/paths, unsupported file extensions, rooted/traversal/alternate-stream/device paths and linked paths are rejected. A source file mismatch rejects the package. Its captured bodies and manifest are immutable; subsequent disk edits cannot alter an already reviewed snapshot.

The package content hash is SHA-256 of UTF-8 text containing the raw manifest SHA-256 followed by ordinal path-sorted lines of `path:rawFileSha256`, separated by newlines. This is an integrity and reproducibility check, not a publisher signature or an assessment of the declared license. The sample includes its original MIT license text.

`Core.Packages.LocalDaxPackageReader.ReadAsync(folder, cancellationToken)` returns `LocalDaxPackage`. `DaxPackageLock.Parse`, `ToJson`, `ValidateDependencies` and `ValidateGraph` support structured inspection. `Semantic.Packages.DaxPackageService` captures the model lock and returns `AuthoringPreview` from `PreviewInstall(package)` or `PreviewRemove(id)`.

Installation/update checks the existing UDF authoring syntax/name rules, language diagnostics, compatibility, unowned name collisions, package ownership and exact dependency version/hash pins. Query/statement lists and calls outside the built-in catalog or captured model/package functions are blocked by this bounded prototype. It verifies that dependency-owned functions still match their lock. A package calling another installed package without declaring its dependency is blocked. Cycles, missing pins and dependent packages that require a different version/hash block the plan.

Updates/removal will not overwrite a package function that was locally edited, renamed or deleted. Removal, or an update that drops a function, is blocked while another model expression still calls that function. Caller inspection includes the exported model DAX properties and ignores string/comment text. External query files and consumers outside the loaded model cannot be discovered by this local check.

## Lock, review and Undo

The exact package lock is stored in the model annotation `PbiBench.PackageLock.v1` in the **same native Undo batch** as the function changes. It includes ID, version, declared license, content hash, dependency pins and expression/description/hidden-state hashes for owned functions. The review shows the complete old/new lock and every affected function. Stale, consumed and wrong-model previews are rejected by the existing authoring boundary. No model save or remote deployment happens during apply.

Use **Export lock JSON** to write `pbibench.packages.lock.json` into the project and inspect its Git diff. File export is explicit: the app does not pretend that native model Undo can automatically reverse a separate disk artifact. After Undo, export the restored lock again when appropriate. Other local files, unlisted package artifacts and project contents are not installed or executed.

## Bounded TE2 Function Undo correction

A native regression demonstrated that TE2 2.28.0 appends a restored function, changing serialized order when unrelated functions follow it. PbiBench's package removal made this existing behavior visible. The original pinned source remains commit `75f10e331b8de0dda5c213180b9b8867b4a38191`.

`vendor/patches/te2-2.28.0-function-undo-order.patch` is a separate original correction to the MIT TE2 `UndoAddRemoveAction`. It captures the original index for **Function objects only** and restores the affected collection suffix through the existing `Undelete` path. Public TOM lacks positional insertion and prohibits reattaching removed metadata, so this path renews the removed TOM objects while retaining their TE2 wrapper identities and complete serialized metadata. Other object types and the existing remote-write review patch are unchanged. No runtime hosting or dependency changes are involved.

`scripts/update-te2-2.28.0.ps1` applies the new patch after the existing patch, checks whether it is already applied, and rejects conflicts. A fresh supplied offline source copy accepted both patches; a second invocation verified idempotence. The actual pinned checkout also passed reverse-application verification. Evidence is `artifacts/v96-prototype-tests/patch-offline-proof.txt`.

## Verification

Focused results on 2026-09-05:

| Scope | Passing executions | Evidence |
| --- | ---: | --- |
| Core compiler/package tests, net10.0 | 29 | `prototype-core-net10.trx` |
| Core compiler/package tests, net48 | 29 | `prototype-core-net48.trx` |
| Native compiler/package authoring, net48 | 8 | `prototype-native.trx` |
| WPF prototype view, net48 | 2 | `prototype-view.trx` |

All evidence is under `artifacts/v96-prototype-tests`. Tests exercise unsupported syntax/semantics, YAML quoting/literal blocks, duplicate and alias lineage guards, executable/path rejection, raw-byte hashes, immutable captures, bounds/cancellation, exact dependency pins/cycles, explicit table mapping, collisions, stale/consumed previews, CL1600 rejection, package caller/local-edit guards, and exact install/update/remove Undo/Redo with unrelated functions interleaved. WPF checks verify explicit table selection, preserved drafts and rejection of stale background compilation results. The pinned Debug upstream solution was rebuilt before the passing native regression. The full V9.6 gate independently covers the complete app and legacy tests.

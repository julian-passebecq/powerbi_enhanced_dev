# V9.3 model authoring

PbiBench's Model tools page adds original draft editors over the existing TE2 2.28 model. DAX Scripts, UDFs, calendars, perspectives, translations and diagram edits use exact previews, session ownership, a model fingerprint, postcondition checks and one local undo batch. Saving or deploying remains a separate operation. The original TE2 model editor, script editor and native BPA remain available.

## Metadata editors

Calendar drafts map primary and associated time categories, time-related columns and explicitly requested sort columns. They generate a sample calculation and a validation query. The public TOM calendar metadata requires compatibility level 1701; PbiBench reports an unsupported model and never upgrades it automatically. The draft editor can validate metadata locally, but only a supporting populated engine can validate calendar data semantics.

Perspectives use an editable membership matrix. A partial table checkbox expands into the explicit membership of its columns, measures and hierarchies. Translations use draft cells across cultures for captions, descriptions and supported display folders; JSON import previews only supplied cells and preserves omitted values. Removing a culture first clears translated cells through TE2's undo-aware indexers so deletion, Undo and Redo preserve values. Calendar reference lists are also edited through undo-aware operations.

Public requirements and interfaces:

- [Microsoft calendar time intelligence](https://learn.microsoft.com/en-us/power-bi/transform-model/desktop-time-intelligence).
- [TOM Calendar](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.calendar?view=analysisservices-dotnet).
- [TOM TimeRelatedColumnGroup](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.timerelatedcolumngroup?view=analysisservices-dotnet).
- The pinned MIT TE2 wrappers provide perspective, culture and translated-property editing and the existing undo infrastructure.

## DAX authoring

The UDF workbench edits supported metadata at compatibility level 1702. Rename resolves callers by source span and preserves comments, strings and unrelated namespaces. It prepares query-local tests; it does not claim an engine successfully ran them. DAX Scripts use an original versioned text format covering measures, calculated columns/tables, calculation items, functions and dynamic format expressions. The document can export selected objects, parse individual properties, preview selected changes and apply one undo batch. Omitting a definition never deletes an object.

Model-wide literal/regex search and replace covers expressions, dynamic format strings and optional descriptions. DAX Explain presents a bounded dependency tree, variables and selected expressions with explicit query context; it is not a step debugger. Function behavior follows Microsoft's public [FUNCTION statement](https://learn.microsoft.com/en-us/dax/function-statement-dax), while the original source-span language service remains advisory rather than a full engine type checker.

Occurrence editing supports Ctrl+D and Ctrl+Shift+L, grouped typing/paste/delete and one native editor undo operation. The tests exercise the actual retained FastColoredTextBox input path, including adjacent selections and line boundaries. This adds editor behavior without replacing the native hosting/runtime architecture.

Previewable code actions include query-local measure/UDF definitions, dependency-ordered UDF definitions, qualified references, local-variable rename, extraction to a local VAR at the existing evaluation point, and inlining proven constant variable uses. Extraction excludes ambiguous operator subtrees, filter/reference-only arguments and schema labels. Inlining retains the declaration and comments and excludes volatile or context-dependent initializers. Dependency expansion is bounded and rejects cycles, invalid bodies and conflicting query overrides. Arbitrary UDF inlining and a full semantic optimizer remain outside this conservative action set. Every text action rejects stale source text or document versions.

Entering Model tools accepts the pending native expression and refreshes metadata. Existing dirty metadata drafts are retained and marked stale rather than silently replaced. Navigation resolves stable model identity, so identically named columns in different tables do not navigate to the first name match.

Diagram, relationship and Table Group semantics and sources are documented in [V9_DIAGRAM_AUTHORING_REFERENCE.md](V9_DIAGRAM_AUTHORING_REFERENCE.md). No proprietary TE3 code, internal formats or assets are used.

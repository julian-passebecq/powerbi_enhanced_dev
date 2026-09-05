# V9.3 DAX authoring

The PbiBench DAX authoring workbench is an original implementation using the pinned open-source TE2 2.28 public wrappers and the PbiBench language service. It does not import a proprietary editor format or implementation.

## Public semantics and model boundary

- Microsoft's [FUNCTION statement](https://learn.microsoft.com/en-us/dax/function-statement-dax) supplies the public DAX query UDF syntax. Query-scoped tests declare the edited function in a generated `DEFINE` preamble and evaluate an explicit scalar or table-valued call; they never change model metadata.
- Microsoft's [DAX UDF guidance](https://learn.microsoft.com/en-us/dax/best-practices/dax-user-defined-functions) describes reusable functions and parameter hints. Model function creation requires compatibility level 1702; PbiBench rejects an incompatible model and never upgrades it as a side effect. The editor accepts public parameter hints supported by its original tolerant parser; the connected engine remains authoritative for complete expression/type validity.
- Microsoft's [VAR reference](https://learn.microsoft.com/en-us/dax/var-dax) defines the value captured by a variable. Extraction wraps an eligible complete expression at its exact source position, and never hoists it across a row/filter context. Constant-use inlining is restricted to literal initializers; it preserves the declaration and comments. Model references, volatile calls, potentially failing computations, ambiguous subtrees and reference-only argument positions are excluded.

UDF create/edit and namespaced rename prepare exact local changes. Renames bind caller tokens by model identity, preserving comments, strings and other namespaces. Native automatic fixup is suspended only while setting the function name; explicit previewed callers are updated through normal TE2 expression setters in the same shared undo transaction. Model-wide text replacement supports bounded literal/regex search, case and whole-word choices, optional descriptions and selected-object scope. Dynamic format changes show the corresponding static-format clear that TE2 would otherwise perform implicitly.

All model changes use `AuthoringPreview`: handler identity, the model fingerprint, single-use preview, one native undo batch, final-state validation and rollback. They do not save, deploy or refresh a server. Wrapper governance still applies. This suite verifies local metadata transactions, not successful deployment to every engine/version.

## Original DAX Script format

Export all or selected objects to an editable `.daxscript` document. Definitions end with top-level semicolons; quoted text, comments and nested separators are preserved. Serialized source round-trips the exact expression text and escaping.

```dax
MEASURE 'Sales'[Revenue] =
SUM ( 'Sales'[Amount] )
;

FUNCTION Finance.Tax =
(value : NUMERIC) => value * 0.2
;

FORMATSTRINGEXPRESSION MEASURE 'Sales'[Revenue] =
"#,0.00"
;
```

Supported expression scopes are MEASURE, COLUMN (calculated columns), TABLE (calculated tables), CALCULATIONITEM and FUNCTION. FORMATSTRINGEXPRESSION applies to measures and calculation items. Missing definitions never delete objects. Parse/select prepares partial apply; the subsequent preview displays property-level differences. New objects require existing containing tables/calculation groups, and the guard rejects partial apply that omits a newly declared dependency. Create a new object first before adding its dynamic format property in a separate preview. Calculated-table schema and data require later engine validation/refresh.

File operations preserve incomplete drafts independently of model apply. Files are UTF-8, capped at 16 MB, written to a sibling temporary file and atomically replaced. Canceled writes preserve the previous destination.

## Explain and code actions

Explain displays recursive model dependencies with cycle detection, an eight-level traversal limit and a 250-node budget, plus callers, VAR declarations and diagnostics. A selected standalone subexpression or complete draft can be evaluated with explicit scalar/table mode and optional CALCULATE/CALCULATETABLE filter arguments. Generated DAX is visible before execution; local VAR uses require their enclosing definition. Results use the existing cancellable read-only query service and a separate connection. This is an explain workbench; it is not a step debugger. Other unsaved local metadata is not deployed automatically for diagnostic queries.

Code actions produce exact source edits guarded by document id, version and original text. Extraction additionally requires reliable expression boundaries and excludes CALCULATE filter positions, schema labels, UDF parameter declarations/defaults, context modifiers and unknown/reference-only argument contexts. Inlining substitutes only resolved uses of a constant and does not remove its declaration. UDF dependency expansion copies UDF/measure definitions in dependency order, bounded to 64 objects, 16 levels and 256 KiB. Cycles, invalid definitions and conflicting query-local overrides suppress this action; table/column data remain engine references.

Scoped verification: 74 language/script tests on each runtime, 13 native DAX authoring tests, and an App Release build with zero warnings/errors. Evidence is in `artifacts/v9-authoring-tests/dax-language-v93-net10.trx`, `dax-language-v93-net48.trx` and `dax-authoring-semantic.trx`. The native tests include exact round-trips, partial apply, multi-scope creation, rollback, dynamic format side effects, UDF namespace rename/undo, stale plans and stable navigation for same-named calculated columns.

# TE3 capability benchmark -> independent PbiBench implementation

Public TE3 documentation is useful as a product requirements benchmark.

## Build next

### 1. Data Preview
Original PbiBench implementation.

Connected-mode table preview:
- small initial page
- virtualized grid
- sort/filter
- multiple tabs
- use DAX `WINDOW` paging for Import tables when engine + key support it
- fallback to first N rows when pagination is unavailable
- clear DirectQuery limitations.

This should be a first-class `Data` surface and table context action.

### 2. DAX Query tabs
Routine work inside PbiBench:
- multiple query documents
- `.dax` files
- F5 execute
- Shift+F5 current/selected statement
- multiple EVALUATE result tabs
- row limit
- DEFINE MEASURE convenience
- result grid
- saved PBIP `DAXQueries` integration.

Deep performance stays in DAX Studio.

### 3. Code Assist / Code Actions
Original implementation:
- Go to definition
- Peek definition
- format
- define measure in query
- inline temporary/query-defined measure
- common safe DAX rewrites
- apply-one / apply-all preview.

Never auto-refactor without showing the DAX diff.

### 4. DAX Script document
Multi-object editing:
- measures
- calculated columns/tables
- calculation items
- UDFs where supported

Changes must map to semantic object diffs before apply.

### 5. UDF workbench
First-class support:
- create/edit/delete
- namespace view
- dependencies
- callers/callees
- rename
- inline
- define with dependencies
- Git-friendly per-file serialization
- compatibility-level check (1702+)
- unsupported target warning.

A future `PbiBench UDF Library` can be original and local-first.

### 6. Calendar editor
Enhanced Time Intelligence:
- map columns to calendar time units
- associated/sort columns
- time-related column flags
- real-time validation
- DAX sample/test generation.

### 7. Perspective editor
Build a fast multi-perspective matrix:
- tri-state selection
- search/filter
- hidden-object toggle
- bulk assignment
- BPA integration.

### 8. Translation editor
Matrix:
- culture columns
- names
- descriptions
- display folders
- missing translation detection
- export/import.

### 9. Advanced refresh
Connected feature:
- scope model/table/partition
- refresh type
- max parallelism
- incremental policy
- development override profiles
- export exact TMSL before execution
- background task queue.

### 10. Diagram+
Continue our original diagram:
- context actions for related/filtering tables
- relationship edit/invert/activate/deactivate
- star-schema auto-arrange
- all/key/no-column display
- data type icons.

## Defer / route externally

### DAX debugger
TE3's debugger works by generating queries for subexpressions/evaluation contexts.
Do not chase full parity now.

Build later:
- DAX Explain
- variable/subexpression query generation
- filter/evaluation context inspector

and route deep work to DAX Studio.

### DAX Optimizer
Do not copy.
If an official API/integration is licensed later, add an adapter.

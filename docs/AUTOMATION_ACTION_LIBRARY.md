# Automation Action Library

The uploaded PBI-Pimp / Toolbox material is valuable because it demonstrates the real productivity unit: **small repeatable model transformations**.

PbiBench should convert this idea into a safer typed action system.

## Action contract

Every action implements:

```text
Id
Category
Title
Description
Risk
Supported contexts
Parameters schema
Scan(model) -> Findings
Plan(findings) -> Changes
Apply(changes)
Validate()
Undo()
Idempotency key
```

## Initial Model Standardization actions

### 1. Calendar / date table
Scan:
- marked date table present?
- valid date column?
- contiguous dates?
- relationships?

Apply options:
- create calculated calendar
- generate TMDL/M table template
- mark as date table
- create common attributes/hierarchy

### 2. Explicit measures
Scan:
- numeric columns with default/sum aggregation
- keys incorrectly aggregatable
- existing explicit measures

Apply:
- generate measures using selected policy
- optionally hide base numeric columns
- display-folder strategy
- description/annotation

### 3. Format DAX / measures
- format measures and calculation items
- normalize format strings
- allow external formatter adapter only with visible data-boundary warning

### 4. Time Intelligence calculation group
Parameters:
- calendar table/date column
- calendar/fiscal year
- YTD toggle
- PY / Y-2 / Y-3
- absolute delta
- percentage delta

### 5. Units calculation group
- Actual / K / M
- preserve % and ratio measures
- dynamic format-string strategy

### 6. Measure table
- create one or more measure containers
- hide placeholder column
- optionally reorganize measures by folders

### 7. Last refresh
- table + measure template
- explicit timezone/display policy

### 8. Best Practice Analyzer
- load official TE standard rules
- scan
- show severity/category/object
- apply only safe FixExpression rules automatically
- allow ignore annotations

## Follow-up action packs

### Model hygiene
- Key/ID -> SummarizeBy None
- hidden foreign keys
- IsAvailableInMDX policy
- discourage implicit measures
- format strings
- descriptions
- naming trim/case

### DAX generators
- PY
- delta PY
- delta PY %
- YTD/QTD/MTD
- comparison cards
- dynamic labels/titles
- dynamic format strings

### Advanced model
- perspectives
- translations
- incremental refresh
- calculation groups
- field parameters
- partitions
- RLS/OLS inspection

### Performance
- VertiPaq inventory
- high-cardinality warnings
- unused columns
- expensive DAX query test harness
- DAX Performance Tuner adapter later

## Macro import

Build an importer for TE `MacroActions.json` so users can browse existing macros in PbiBench.

V1 importer:
- parse hierarchy/name
- tooltip
- valid context
- source text
- trust state
- no execution by default.

V2:
- run locally only after explicit trust acknowledgement.

Do not silently ingest and execute internet scripts.

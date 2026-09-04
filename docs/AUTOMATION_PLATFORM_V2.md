# Automation Platform V2

The screenshot/Pimp/Toolbox examples reveal the key product opportunity:

**Turn dozens of useful C# snippets into discoverable, typed, safe, testable product actions.**

## Action categories

### Model Hygiene
- Keys/IDs -> SummarizeBy None
- hidden technical columns
- IsAvailableInMDX policy
- descriptions
- formatting
- display folders
- naming
- implicit-measure policy

### Measure Engineering
- explicit measure from selected numeric column
- bulk explicit measures
- measure table(s)
- measure format normalization
- PY / delta / delta %
- MTD/QTD/YTD
- rolling periods
- dynamic format strings
- label/title measures
- KPI bundles
- generated test query

### Calendar / Time
- date-table discovery
- date table wizard
- fiscal calendar
- mark date table
- time calculation group
- relative date columns/parameters

### Calculation Groups / UDFs
- time intelligence
- units/scaling
- scenario
- currency
- current/prior/budget
- model-aware exclusions
- DAX UDF templates where preferable

### Model Structure
- relationship scan
- cardinality validation
- bi-directional filter review
- many-to-many review
- inactive relationship review
- fact/dimension classification
- grain documentation
- star-schema suggestions

### Performance
- unused columns
- high-cardinality candidates
- column encoding hints
- partition review
- Direct Lake checks
- DAX query benchmarks

### Documentation / Governance
- BPA
- metadata report
- descriptions
- owner/domain annotations
- perspectives
- translations
- glossary/KPI catalog
- lineage export

### Report
- PBIR validation
- visual field-binding validation
- page alignment
- style templates
- accessibility
- unused custom visuals
- visual regression

## Action contract

Every action implements:

```text
Metadata
  Id
  Name
  Category
  Description
  SupportedContexts
  RiskLevel
  RequiredCapabilities

Scan(context)
  -> Findings[]

Plan(findings, options)
  -> ChangePlan

Preview(changePlan)
  -> ObjectDiff + FileDiff

Apply(changePlan, transaction)
  -> ChangeResult

Validate(changeResult)
  -> ValidationResult

Rollback(transaction)
```

## Script compatibility

Support two modes:

### Typed native action
Preferred.

### Trusted TE-style macro
For legacy/private scripts.

Never execute newly imported C# automatically.
Show:
- code hash
- source
- contexts
- declared risk
- object/file changes
- trust state.

## Action recorder

Later:
- listen to the PbiBench command journal,
- select a series of user operations,
- parameterize obvious values,
- save as Action recipe or generated C# scaffold.

This is an original command-recorder implementation, not a copy of another product's recorder.

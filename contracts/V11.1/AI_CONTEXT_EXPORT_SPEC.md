# AI Context Export specification

## Goal

Give any external AI/chat enough context to understand a Power BI semantic model and generate useful DAX/modeling suggestions without requiring an embedded provider or account integration.

The output is a normal `.zip` with transparent files. No proprietary binary context format.

Suggested name:
`<model-name>.pbibench-ai-context.zip`

## Export modes

1. **Model only** - metadata + DAX, no data samples.
2. **Selected scope** - selected tables/measures/relationships plus required dependency context.
3. **Model + samples** - metadata + explicitly selected sampled tables.
4. **Diagnostics** - model + optional BPA/VertiPaq/test/workspace evidence.

Data sampling is OFF by default.

## Pre-export privacy screen

Show exact categories and estimated size:

```text
AI Context Export

Model metadata                 included
Measures/DAX                   included
Relationships                  included
Calculation groups/UDFs        included
RLS expressions                [off by default]
Descriptions                   included
BPA findings                   [optional]
VertiPaq stats                 [optional]
Semantic tests                 [optional]
Git/workspace diff             [optional]
Data samples                   [OFF]
Source/partition expressions   NEVER by default
Credentials/tokens             NEVER
Local machine paths            NEVER unless explicitly required and sanitized

[Review files] [Export ZIP]
```

## ZIP layout

```text
manifest.json
AI_README.md
model/
  model-summary.json
  tables.csv
  columns.csv
  relationships.csv
  measures.dax
  calculated-objects.dax
  calculation-groups.json
  functions.dax
  perspectives.json
  translations.json
  roles.json                 optional / redacted setting
  dependencies.json
samples/
  Sales.csv                  optional
  Customer.csv               optional
quality/
  bpa.json                   optional
  vertipaq.json              optional
  semantic-tests.json        optional
workspace/
  semantic-diff.json         optional
checksums.sha256
```

Keep every file useful without PbiBench. CSV uses UTF-8 and invariant machine-readable values where possible. JSON schemas are versioned.

## `AI_README.md`

Generate a concise model orientation document:

- model name and compatibility level
- storage modes
- table list and row-count metadata if known
- fact/dimension hints only when evidence exists
- relationships and cardinality/filter direction
- measure inventory by table/display folder
- calculation groups/UDFs
- important BPA/VertiPaq warnings if included
- sample-data disclaimer
- exact export scope/omissions

End with a neutral instruction for an external AI:

> Use the attached semantic metadata as authoritative for object names and relationships. Treat sampled rows as examples only, not complete data. Do not invent columns or measures that are absent from the model. When proposing DAX, state which existing objects it depends on.

## Sampling controls

Per table:
- include yes/no
- rows: 0, 5, 10, 25, 50, 100, 250, custom bounded value
- select columns
- include hidden columns yes/no
- optional deterministic key/order column if available

Global hard limits:
- maximum sample rows/table configurable with safe cap
- maximum ZIP size estimate and actual cap
- maximum total sampled cells
- cancellation

Do not pretend a first-N sample is representative. Record sampling method in manifest.

Possible methods:
- `FirstN` with explicit ordering where safe/available
- `TopNByKey`
- `UserFilteredQuery`

Avoid expensive random sampling as a default, especially for DirectQuery/Direct Lake.

## Security exclusions

Exclude by construction:
- connection strings
- access tokens/API keys
- credentials
- partition/source expressions unless a future explicit expert option is added
- gateway configuration
- tenant secrets
- local recovery paths
- hidden authentication environment-variable values

Names, DAX and sample rows can still be sensitive. The UI must state this plainly.

## Optional local proposal import

The existing strict proposal schema can be useful without embedded AI.

Workflow:

```text
Export ZIP
   -> external AI/chat
   -> user asks for measures/refactor
   -> AI returns PbiBench proposal JSON (optional)
   -> Import Proposal
   -> strict parse
   -> exact preview
   -> user approval
   -> apply/undo
```

No proposal can include or create its own approval token. Unknown fields fail closed.

This optional import is secondary. The core V11 deliverable is the export.


## Automation context for external AI

When requested, include a bounded `automation/` section so an external AI can generate automation without needing an embedded provider:

- `automation/README.md`: current Safe Preview vs Trusted C# rules, common `Model`/`Selected` patterns and a few original examples.
- `automation/safe-script-capabilities.json`: generated from the actual safe action/schema implementation; supported scopes, operations, writable properties and bounds.
- optional selected-object inventory relevant to the requested automation scope.

Do not export arbitrary local macro contents or trusted scripts by default. They may contain paths, URLs, business logic or secrets. If the user explicitly includes scripts/macros, list them in the export review as potentially sensitive text.

# BPA strategy

The supplied standard Power BI rule set contains useful categories:
- DAX Expressions
- Formatting
- Metadata
- Model Layout
- Naming
- Performance

## PbiBench architecture

Do not hardcode one giant rule JSON.

Create:
- RulePack metadata
- Rule source/provenance
- version
- applicability
- compatibility level
- enabled/severity override
- fix risk
- suppression

## Rule packs

### PbiBench Core Rules
Independently authored and safe to ship.

### User/Community Rules
Loaded from user-provided JSON.

Because the supplied BestPracticeRules snapshot has no clear license file in the archive, do not assume public redistribution rights.
It can be loaded as a local user rule pack.

## Fix risk

Examples:

SAFE:
- SummarizeBy None on selected non-measure numeric key fields after explicit user scope
- hide proven FK columns if report usage is known

REVIEW:
- floating-point type conversion
- display-folder normalization

BENCHMARK REQUIRED:
- attribute hierarchy / MDX availability changes
- performance-oriented metadata

MANUAL ONLY:
- deleting "unused" columns/measures unless external report/query usage has been fully scanned.

Never blindly apply a `FixExpression` solely because the rule file contains one.

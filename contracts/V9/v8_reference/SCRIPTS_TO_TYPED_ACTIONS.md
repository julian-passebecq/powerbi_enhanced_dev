# MIT TabularEditor Scripts -> PbiBench typed actions

The official/community Scripts repository is MIT and is a strong source for PbiBench automation.

Do not merely copy `.csx` buttons into the UI.
Convert high-value patterns into typed actions with preview/validation/undo.

## Basic
- Generate SUM measures
- Generate COUNTROWS measures
- Format measures
- Hide columns on many side of relationships
- Move columns to display folders

## Intermediate
- Clean object names
- Create Time Intelligence through calculation groups
- Create explicit measures
- Dynamic measure selector
- BIM slimmer
- TMDL slimmer

## Databricks
- Add metadata descriptions
- Create relationships
- Semantic model setup

These fit PbiBench's Databricks/Fabric scenario planner.

## Report/model validation
Use the pattern behind invalid object-reference checks to feed:
- semantic/report dependency QA
- PBIR field-binding validation later.

## Script conversion requirements

Every converted action needs:
- source attribution
- typed supported scope
- scan/findings
- preview
- apply
- validation
- undo
- unit tests

Keep the original `.csx` script available only in an optional trusted script library if useful.

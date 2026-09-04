# Fabric / Direct Lake Specification

## Fabric Import Wizard

Steps:
1. Entra login
2. choose workspace
3. choose Lakehouse / Warehouse / SQL database / mirrored database
4. choose tables/views
5. preview schema/data
6. choose columns
7. choose storage mode:
   - Direct Lake on OneLake
   - Direct Lake on SQL
   - Import
   - DirectQuery where valid
8. validate model/storage-mode compatibility
9. preview semantic objects/partitions to be created
10. apply
11. validate.

## Direct Lake rules

PbiBench must understand:
- OneLake entity partitions
- SQL-backed Direct Lake paths
- Import partitions
- source schema/table mapping
- transformations lost when converting Import -> Direct Lake 1:1 mappings
- mixed-mode restrictions
- collation/source compatibility
- materialized-view restrictions where applicable.

## Conversion actions

Typed previewable actions:
- Import -> Direct Lake on OneLake
- Direct Lake on OneLake -> Import

Show:
- partitions removed/created
- source mapping
- M/SQL transformation loss warning
- source object match status.

## Preview from Fabric

Use the safest supported path for the connected model/source.
Show capacity/memory warnings for large Direct Lake previews.

## Source schema update

Compare:
- semantic columns
- source columns
- types
- mappings

Classify:
- new source column
- removed source column
- type change
- rename candidate
- mapping mismatch

Never automatically delete semantic objects without explicit approval.

# Report Studio — PBIP / PBIR

## Separate process

Create:
`PbiBench.ReportStudio`

Recommended:
- `net10.0-windows`;
- no TE2/TOMWrapper/App/ModelEditor reference.

Shared pure libraries:
- PBIP discovery/project contracts;
- PBIR index/validation/change plan;
- local semantic/report lineage.

## Open

Support:
- `.pbip`
- `.Report/definition.pbir`
- report folder
- PBIP root discovery

Discover sibling semantic model / TMDL where available.

## UI

### Report Explorer
- report
- pages
- visuals
- bookmarks
- filters
- resources
- report extensions/report measures where format exposes them

### Wireframe
Not a Power BI renderer.

Render:
- page bounds
- visual rectangle
- visual type/name
- title
- z-order
- hidden state
- selected semantic fields/measures

### Inspector
- object ID/name/type
- position
- semantic references
- filters
- common properties
- annotations
- schema/version
- raw JSON read-only

### Bottom
`Changes | Validation | Lineage | Git/Disk`

## Safe change engine

Every write:
`configure -> exact ReportChangePlan -> preview -> backup -> atomic apply -> schema validation -> Git/disk diff`

Preserve unknown JSON properties.
Reject stale source hashes.

PBIR disk editing does not have TE2 model Undo. Use backup + restore + Git.

## PBIR sensitivity

Warn that report metadata can include persisted semantic values such as slicer/filter values.
Do not include `.pbi/localSettings.json` or cache files in exports/context bundles.

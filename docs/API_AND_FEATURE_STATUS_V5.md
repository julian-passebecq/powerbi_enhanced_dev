# API / Feature Status Snapshot — 2026-09-04

This status file must be revisited because Fabric evolves quickly.

## Stable/core surfaces to design around
- Fabric REST API v1 core item/workspace APIs
- XMLA/TOM semantic model management where capacity/settings permit
- Power BI REST API
- Power BI External Tools registration
- DAX Studio external process integration
- TMDL/TOM

## Preview / caution surfaces
- Fabric Admin List Items is currently Preview
- Fabric Core MCP Server is Preview
- Power BI Desktop Bridge is Preview
- PBIP/PBIR remain preview in current project documentation context
- some item definitions / item types vary in support

PbiBench must show feature status in the UI and degrade gracefully when a preview API is unavailable.

## Important PBIP external editing constraints
- external reload applies to PBIP, not PBIX
- Desktop Bridge is preview
- `cache.abf` isn't externally reloaded
- some files are not externally editable during preview
- `unappliedChanges.json` can cause external expression edits to be lost
- keep path length under Windows limits.

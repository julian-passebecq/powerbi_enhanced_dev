# V9.3 Diagram and Table Groups

This implementation uses PbiBench's existing WPF diagram and the pinned open-source TE2 2.28 public wrappers. No proprietary editor code, assets or internal formats are used.

## Public behavior and local validation

- [Microsoft TOM SingleColumnRelationship](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.singlecolumnrelationship?view=analysisservices-dotnet) defines `OneDirection` as To filtering From, bidirectional filtering, engine-resolved Automatic, cardinality, active state, security and date joining. The diagram leaves Automatic arrows unresolved. Inverting endpoints with OneDirection explicitly warns that the filter and security flow reverse.
- [Model relationships](https://learn.microsoft.com/en-us/power-bi/transform-model/desktop-relationships-understand) requires different tables and matching data types and explains regular/limited relationships, alternate filter paths and data uniqueness. The editor rejects self-table endpoints, mismatched/unsupported column types, duplicate column pairs, parallel active table relationships, invalid enums, unsupported compatibility levels, and incompatible one-to-one/security/date settings. An alternate active path is reported for review because engine ambiguity depends on direction, priority and query context.
- [Composite models](https://learn.microsoft.com/en-us/power-bi/transform-model/desktop-composite-models) explains source groups and cross-source limited relationship semantics. The editor reports these as engine/source constraints; it does not infer source-group identity solely from storage modes or claim metadata establishes data uniqueness.
- [Assume referential integrity](https://learn.microsoft.com/en-us/power-bi/connect-data/desktop-assume-referential-integrity) documents DirectQuery join optimization and the null/unmatched-row requirements. This editor's enablement path is explicitly restricted to tables whose partitions all use DirectQuery. Direct Lake configuration remains a later service workflow. No preview claims to have checked source data or enables the setting silently.

Every relationship preview captures the actual TE2 relationship and columns. Relationship `ID` is the stable underlying metadata name; TE2's displayed `Name` is derived from endpoints and can change. Endpoints are temporarily cleared inside one undo batch when changed, because TE2 correctly vetoes a direct assignment to the existing opposite endpoint. All final fields are checked against the displayed request; the shared authoring transaction rolls back setter vetoes or failures. Preview ownership, full model fingerprint, single-use semantics and current-session guards prevent applying stale edits. No editor action saves, deploys, refreshes or changes a remote model.

## Table Groups

Groups are an original PbiBench virtual organization feature. Each assigned table has one owned annotation named `PbiBench.TableGroup`, encoded as bounded JSON `{"version":1,"group":"Finance"}`. A name is trimmed, at most 256 characters and contains no control characters. The parser caps input at 4096 characters and depth 4; unknown versions or shapes are preserved and reported instead of overwritten. Assign, remove and rename affect only this annotation and run through the shared preview and TE2 undo batch. Table renames preserve group membership because the annotation belongs to the table rather than a name-keyed external map.

## Diagram interaction

The diagram retains the actual table and relationship object references. It supports all/key/no-column display, type labels, hidden-column indication, active/dashed lines, cardinality, To-to-From arrows, relationship selection and editing, inversion/activation previews, one-hop related/filtering table views, group filtering and group-aware inferred star layout. Filtering neighbours include only active resolved directional relationships; Automatic direction is disclosed as unresolved. Existing zoom, pan, fit and search remain available.

The automated suite exercises metadata previews, exact changes, group bounds/round-tripping, identity preservation, stale/foreign sessions, compatibility/type/cardinality/security validation, atomic endpoint inversion, undo/redo and graph navigation direction. These tests do not represent server deployment validation or populated-model data checks.

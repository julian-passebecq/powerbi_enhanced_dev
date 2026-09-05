# V9 Fabric table authoring

PbiBench's Fabric workspace uses captured public source schemas to prepare local TE2 metadata edits. Sign-in, discovery and source preview use the Fabric adapter described in `V9_FABRIC_REFERENCE.md`. Import never deploys, refreshes, or queries the model as a side effect. Review and Apply use the same fingerprint-bound, single-use `AuthoringPreview` and native Undo transaction as the other model tools.

## Modes and mappings

The wizard supports Direct Lake on OneLake, Direct Lake on SQL, Import and DirectQuery. Select source columns and an explicit target table name before reviewing all table, column, partition, source expression and annotation changes. Import/DirectQuery create M partitions with a discovered SQL endpoint and exact schema/item navigation. Direct Lake creates entity partitions with a shared M source expression. SQL endpoints are never guessed from display names, credentials are never embedded, and connection strings are not accepted as endpoint hostnames.

OneLake requires discovered Delta schema provenance; SQL metadata alone does not establish Delta compatibility. OneLake permits composite Import/DirectQuery tables. SQL-backed Direct Lake requires a Lakehouse/Warehouse database GUID, one source, and compatible existing storage modes. Unknown existing Direct Lake expressions block automatic mixing. A SQL view may need SQL DirectQuery fallback; a non-materialized SQL view cannot become a OneLake entity. Delta-backed materialized views are accepted when discovery verifies the backing table.

Direct Lake requires compatibility 1604 or later. The wizard never upgrades compatibility or changes collation implicitly. OneLake sets `DirectLakeBehavior=DirectLakeOnly` as an explicit preview row when needed. Review source and model collation, cloud connection identity, OneLake security, region and capacity limits. Local metadata validation cannot prove cloud permissions or framing success. Native deployment may frame a newly added Direct Lake table; the host's existing remote-write review identifies that effect.

Source integers map to Int64, floating numbers to Double, strings to String, booleans to Boolean, and supported date/time types to DateTime. Decimal/numeric maps to Double with an explicit precision warning; SQL money maps to fixed Decimal. SQL GUID maps to text only for Import/DirectQuery with a warning. SQL `timestamp` is rowversion/binary, whereas Delta `timestamp` is a date/time value. Unsupported binary, complex and Direct Lake GUID types are errors. New columns use explicit source mappings and `SummarizeBy=None`.

## Conversions and source drift

Import → OneLake and OneLake → Import show the full serialized metadata of each removed partition, then the replacement source/partition. Partition filters and M/SQL transformations are not carried across. Exact mapped columns and types are required; missing mappings, refresh policies, unsupported calculated-column contexts, and Direct Lake hierarchies block conversion. Unused shared expressions remain available. A native unique replacement partition is created before removing old partitions because TE2 preserves at least one partition per table. All changes remain one Undo step.

Schema comparison identifies new source columns, removed source columns, type changes, mapping mismatches and possible renames. Only selected additions/type changes are applied. It never deletes or renames a semantic column automatically. Type changes involving a relationship or Sort By dependency require separate review. Existing unrecognized/custom M transformations are not treated as verified source mappings. Source fingerprints bind item identity, endpoint, schema/table, format, columns, types and ordinals.

## Verification and limits

Native tests exercise all four modes, complete-model Undo/Redo, partition transformation-loss previews, source provenance, storage-mode conflicts, schema drift, relationship guards, compatibility and M string escaping. Launch checks exercise the actual Fabric page, read-only preview and native apply/Undo using an explicitly labeled offline schema fixture.

No populated Fabric item is available in this environment. Real Entra consent, catalog discovery, source row preview, cloud deployment and Direct Lake framing remain external integration validation pending. No fixture result is described as a successful cloud operation. Complex M interpretation, automatic upstream transformation migration, collation migration, and automatic schema deletion are outside this pass.

Public sources, reviewed 2026-09-05:

- [Microsoft Direct Lake overview](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview): storage modes, compatibility, constraints and security.
- [Microsoft Direct Lake operation](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-how-it-works): framing and capacity behavior.
- [Microsoft public semantic authoring examples](https://github.com/microsoft/skills-for-fabric/blob/main/plugins/powerbi-authoring/skills/semantic-model-authoring/references/direct-lake-guidelines.md): public TOM entity/source representations.
- [Sql.Database](https://learn.microsoft.com/en-us/powerquery-m/sql-database) and [M lexical structure](https://learn.microsoft.com/en-us/powerquery-m/m-spec-lexical-structure): generated source navigation and escaping.

Implementation is original PbiBench code over the pinned open-source TE2 wrapper and public Microsoft APIs. No proprietary TE3 code or assets are used.

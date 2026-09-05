# V11.2 — Feature Map and provenance UX

This focused pass starts from `675a5026749094206339c312b7f190964953ce57` and sets the product version to `11.2.0`. It changes product metadata, the existing Apps / Tools / About surface, documentation and relevant tests. Existing Fabric, C#, Agent, DataForge, DAX, PBIR, TE2 and framework implementations are preserved.

## Product view

Open **Apps / Tools → Feature Map / Provenance / About**. The first tab shows 21 high-level areas with Feature, Status, Origin, Our implementation, TE3 comparable capability and Focus. The original detailed Provenance / About table remains on the second tab. Opening this window requires neither a model connection nor a network request.

- **All** includes core, companion, external, utility, labs, future and gap rows.
- **Core** selects Core rows.
- **Companions** includes Companion and External rows.
- **Labs** includes Labs, Future and all rows with Freeze focus.
- **TE3 gaps** includes Partial and Gap comparisons.

Selected-row details explain the scope, UI location, limitations and joined component owners/update lanes. Status is distinct from focus: DataForge is an existing Companion with Freeze focus; the debugger is a Gap with Later focus. Freeze means preserve the existing implementation without expansion. Future/gap entries with no implementation have empty provenance references and explicitly say that no implementation provenance is claimed.

**Open detailed catalog** reads the bundled `docs/architecture/FEATURE_CATALOG.md` into a read-only document window. It does not navigate online or launch a companion. The package and ordinary app build outputs contain the architecture documents.

## Sources and regeneration

`docs/architecture/feature_catalog.json` owns high-level product descriptions, status/focus, limitations and public capability comparisons. `docs/architecture/provenance.json` remains the authoritative ledger for source type, upstream, license, pin, adapter, local patches, update lane and protecting tests. The Feature Map derives concise Origin labels from its referenced provenance components; the feature JSON does not duplicate that legal/dependency ledger.

`Core.Platform.FeatureCatalog` parses the bundled metadata with a 256 KiB cap, 1–64 high-level rows, bounded enum vocabularies and text/list limits. It rejects duplicate IDs/JSON fields, unknown fields, broken/duplicate provenance links, version/baseline disagreement and invalid comparison evidence URLs. Existing feature/provenance metadata is embedded in Core; UI code has no network transport.

Generate the detailed document deterministically:

```powershell
dotnet run --project scripts/FeatureCatalogGenerator -c Release -- .
```

The tool uses the same pure `ToMarkdown` method as the tests. It joins the two source files and writes UTF-8 Markdown with LF newlines. `FeatureCatalogTests` compares the generated text with the checked-in document and compares disk metadata with the embedded resources on both target frameworks. Deliberate source changes therefore require regenerating the document before CI can pass.

## Public comparison baseline

The comparison uses **Tabular Editor 3, version 3.26.3, verified 2026-09-05**. The version is supported by the [official downloads page](https://docs.tabulareditor.com/en/references/downloads.html). Capability references use only the supplied official [migration comparison](https://docs.tabulareditor.com/en/getting-started/migrate-from-te2.html), [UDF documentation](https://docs.tabulareditor.com/en/tutorials/udfs.html) and [roadmap/shipped history](https://docs.tabulareditor.com/en/references/roadmap.html).

Comparison labels are PbiBench assessments of broad public workflows, not provenance, competitive scoring or tested parity. The CLI row explicitly refers to the separately documented TE CLI preview. Edition/connection limits apply; an informational gap is not a commitment to implement it. No TE3 binaries, proprietary code or assets were obtained or reused.

## Relevant Release gate

```powershell
./scripts/invoke-v11-gate.ps1 -Configuration Release -Scope FeatureMap
```

This builds the solution, runs catalog/provenance tests on net10 and net48 plus focused WPF Feature Map tests, packages into a fresh directory, and runs the packaged Semantic IDE and Toolbox launch smokes. The Semantic IDE smoke now checks the map, filters, existing Provenance tab and packaged document against the embedded catalog, and captures `v11-feature-map.png`. The gate records Scope and Configuration in `gate-result.json`.

The default `-Scope V11` broader impacted gate remains available. Existing hosted workflows discover the new portable tests automatically. They do not claim main WPF/native TE2/package GUI or live Power BI/Fabric validation. No live model or tenant is needed for this change.

The stale V11.1.1 verification report is corrected with its published SHA and the two confirmed successful hosted runs; its 227 local test executions remain distinct from hosted results. V11.2 post-push CI inspection is left to the separate reviewer.

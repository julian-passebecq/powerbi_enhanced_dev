# V2 Pass 3 — unified product

Baseline: `70493e1a63064a7e6d2ec98c285d187a556834a3` (current main verified before editing).
Product: PbiBench 2.3.0. The supplied 13-file Pass 3 pack was read in full. Its proposed workstreams were implemented within the user's scope; its historical Pass 1/2 material was treated as baseline/reference material.

## One product, isolated modules

Start `PbiBench.exe`. Home groups Continue, Build, Project, Platform and Specialist workflows. The compact rail offers Home, Model, DAX, Automate, Report, Project, Fabric, Tools, Settings and About. Model tools, data, semantic diagram, quality and Fabric model-source authoring remain secondary workspaces; the existing Ctrl+K palette retains navigation to the deeper workflows.

Report and Fabric launch/focus child processes. Repeated identical launches from the current PbiBench session focus the existing child; changed handoff context opens a separate window. Children started independently are not adopted. Nothing embeds their modern WPF windows into net48.

The shared UI-only `PbiBench.DesignSystem` supplies light tokens, Segoe UI, standard buttons and 14 original vector icons. Its original artwork license and inventory are shipped. No icon font, Data Goblin asset or TE3 code was used. Existing TE2 WinForms controls remain intact.

## External tools correction

`PbiBench.ExternalTools` is net10.0/net48 and owns process requests/adapters, Windows quoting, executable validation/discovery, the companion catalog, local metadata DTOs and component discovery. It has no project reference, UI, authentication, semantic model or DAX dependency. `PbiBench.DaxStudio` now references only ExternalTools and retains the existing DAX-specific locator definitions, arguments, scratch query and explicit dscmd handoffs. Its functional capabilities are unchanged.

Report Studio and Fabric Toolbox now reference ExternalTools directly. Neither child references or ships DaxStudio. Report Studio remains TE2/Fabric-auth free; Fabric Toolbox remains TE2/Semantic-UI free. Pass 2 Report Studio editing, impact review, Gallery provenance, PBIR write policy and Fabric read-only report snapshots remain intact.

`module_catalog.json` includes the exact project-reference graph, primary module owners and capability dependencies. Tests compare every source project/reference, module closure and forbidden dependency. This also identifies portable Workspace/Core services separately from the semantic UI ownership group.

## Design Exchange v1

Open **Project > Design Exchange** or the Home card. Export `pbibench-model-context.json` from the current model, or open an existing context. The exporter reuses `AIContextCapture`/`ContextObject`, projecting only metadata, DAX, formatting and relationships. Calculation group/item and function metadata are included where available. It excludes partition/source expressions, credentials, connection strings, gateways, roles, raw rows and path fields by construction. DAX, names and descriptions remain user-authored metadata and can themselves contain sensitive text; the export JSON is available for review.

Optional reviewed statistics/sampling remain in the existing **Tools > AI Context Export** utility. They are not silently incorporated into the Design Exchange v1 metadata file. No provider SDK, network call or embedded AI was added. **Copy Design Prompt** produces provider-neutral instructions for separate dashboard-spec and theme JSON.

Dashboard validation is exact-case and fails closed on unknown or duplicate fields, executable expression/script fields, unsupported contract versions, null/missing required structure, excessive counts and invalid coordinates. Limits: 4 MiB JSON, depth 32, 32 pages, 100 visuals/page, 1,000 visuals overall, 12 bindings/visual, canvas/coordinates up to 8,192. A visual requires explicit position or a supported region. Positions must fit the page. IDs are unique across the design. Binding kind and exact table/object names are checked against the fingerprinted metadata. Model fingerprints include the exported metadata and are verified when loading context.

An explicit `unbound: true` design omits `modelFingerprint`, labels every binding unverified and cannot authorize report changes. Fingerprint mismatches in bound designs are blocking. Unknown visual kinds remain visible as unsupported placeholders; no arbitrary PBIR visual is synthesized.

Theme validation uses the unchanged Microsoft Power BI Desktop 2.156 / theme 5.75 schema, pinned to `6ccd62e9d79c4b1b0662ba8955598492c35cc8c4`. Source hashes/license are in `schemas/report-theme.lock.json` and `schemas/report-theme/`. Validation never fetches a declared `$schema` URL or remote reference. The UI shows schema version/validity, name, bounded colors, recognized visual-style families and diagnostics. It does not resolve image URLs, download fonts or evaluate theme expressions. Schema permissiveness for custom visual properties is Microsoft's; unsupported families are diagnosed.

Validated model/spec/theme paths pass through module handoff v1. The sender revalidates current file bytes on Open; Report Studio independently reads bounded files and revalidates them. A stale or failed load clears the prior design preview. Report Studio displays a separate **Design Preview** workspace with proposed layout, page selection, binding evidence, theme swatches and diagnostics. Region layout is approximate and may overlap at high density. The preview constructs no ReportChangePlan and cannot mutate PBIR. The existing Report engineering workspace retains its independent preview/apply/restore workflow.

## Project/package contracts

`ProjectContext` v1 carries local paths, fingerprints, source (Disk/Loaded/Live), Git summary and explicitly selected Fabric IDs. It contains no runtime objects or secrets. Receivers show that context; Fabric sign-in and inventory loading remain explicit. Handoffs are local files under the existing per-user PbiBench settings directory.

`components.json` declares Semantic IDE 2.3.0, Report Studio 2.3.0 and Fabric Toolbox 0.4.0 paths/versions plus ExternalTools/PBIR contract versions. Tools and About read it. Packaging preserves runtime folders, attribution and component metadata. `test-components.ps1` checks the declared paths against the actual managed binary versions. No fourth launcher executable was created.

## Verification scope

Targeted implementation checks cover both contract runtimes, exact dependency closure, shell WPF, Report Studio design preview and unchanged specialist arguments. The final impacted Release gate is `scripts/invoke-v2-gate.ps1`; it builds the solution, runs V2/relevant V11/net48 regression suites and new contracts, checks process isolation, packages components and runs the semantic/report/toolbox offline smokes. Post-push hosted CI and architecture audit belong to the external reviewer.

No authenticated live Fabric/Power BI/XMLA, Desktop rendering, DAX Studio query or Bravo execution is claimed. No DAX Studio/Bravo feature expansion, remote PBIR writes, embedded AI or MCP work was added.

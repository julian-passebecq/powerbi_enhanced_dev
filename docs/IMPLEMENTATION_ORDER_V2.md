# Implementation order V2

## Phase 0 — source/legal/build baseline
1. Vendor TE2 snapshot.
2. Copy license/notices.
3. Audit third-party licenses.
4. Build TE2 unchanged.
5. Run tests.
6. Record failures before modifications.

## Phase 1 — semantic core
1. Bring BPALib into modern solution.
2. Create modern TOM abstraction.
3. Port/wrap essential TOMWrapper functionality.
4. Object tree.
5. properties.
6. dependency graph.
7. read-only model connections.
8. characterization tests.

## Phase 2 — automation engine
1. Typed Action API.
2. scan/findings.
3. dry-run diff.
4. transaction journal.
5. selected object contexts.
6. initial 15 model actions.
7. MacroActions importer for trusted scripts.

## Phase 3 — DAX / model UX
1. original code editor.
2. formatter.
3. autocomplete from model metadata/function catalog.
4. DAX query executor.
5. result grid.
6. code actions.
7. model diagram.
8. perspectives/translations.
9. refresh queue.

## Phase 4 — PBIP/Git
1. workspace tree.
2. PBIP/TMDL source-of-truth mode.
3. Git status/baseline.
4. semantic diff.
5. object-to-file mapping.
6. selective stage/restore.
7. validation CLI.

## Phase 5 — report engineering
1. clean-room PBIR model.
2. schema validation.
3. report tree.
4. pages/visuals.
5. bindings.
6. Desktop Bridge.
7. screenshots.
8. report QA.

## Phase 6 — agent
1. MCP client framework.
2. external Modeling MCP.
3. PbiBench high-level MCP server.
4. safe agent plans.
5. audit.

## Phase 7 — Fabric / DataForge / VizForge
1. scenario adapters.
2. DataForge truth assertions.
3. Fabric/Databricks metadata.
4. VizSpec.
5. D3/WebView2.
6. native PBIR mapper.
7. custom visuals.

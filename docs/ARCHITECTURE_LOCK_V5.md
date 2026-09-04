# Architecture Lock V5

## Final product topology

```text
Power BI Desktop ----------- External Tool registration -----------+
                                                                  |
PBIP / TMDL / PBIR / Git -----------------------------------------+
                                                                  v
+-----------------------------------------------------------------------+
| PbiBench                                                              |
|                                                                       |
| Connection Hub | Workspace | Model | DAX | Report | Fabric | Estate   |
| Automate | QA | Git | Deploy | Knowledge | Agent                     |
+---------------------+----------------------+--------------------------+
                      |                      |
             semantic/data plane      cloud/control plane
                      |                      |
        TE2-derived semantic core      Fabric REST
        TOM / TMDL / XMLA              Power BI REST/.NET SDK
        BPA / Actions                   Fabric Admin REST
        DAX queries                     ARM Fabric capacities
                      |                      |
                      +----------+-----------+
                                 |
                           transport router
                                 |
                    +------------+------------+
                    |                         |
              internal execution        specialist bridge
                                            |
                                      DAX Studio / dscmd
                                      Power BI Desktop
                                      VS Code
```

## Why this architecture
- avoids duplicating mature DAX Studio diagnostics,
- avoids constraining the product to TE2's legacy UI,
- uses first-party REST/XMLA/TMDL surfaces,
- supports offline/local and cloud workflows,
- keeps preview MCP optional,
- allows one coherent approval/audit layer over every transport.

## Project boundaries

- `PbiBench.Core` — contracts/domain/tool routing/action plans
- `PbiBench.Workspace` — PBIP/PBIR/TMDL filesystem
- `PbiBench.Semantic` — TE2/TOM/TMDL semantic model layer
- `PbiBench.Fabric` — Fabric REST control plane
- `PbiBench.PowerBI` — Power BI REST/admin adapter
- `PbiBench.DaxStudio` — external specialist bridge
- `PbiBench.Git` — Git process adapter + semantic diff
- `PbiBench.DataForge` — deterministic test-data contracts
- `PbiBench.Agent` — MCP/agent orchestration, later
- `PbiBench.Cli` — CI/headless operations
- `PbiBench.App` — WPF shell

## Rule
UI code does not directly call REST/XMLA/process APIs. All operations go through application services and auditable commands.

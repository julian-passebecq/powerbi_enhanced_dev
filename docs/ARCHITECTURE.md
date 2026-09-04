# Architecture

```text
                        PowerBIBench.App (WPF)
                                |
        +-----------------------+-----------------------+
        |                       |                       |
  Workspace/Git           Semantic Model           Report/Viz
        |                       |                       |
  PBIP scanner          TabularEngine              PBIR Engine
  Git adapter           BPA / Actions              VizForge Bridge
        |                DAX / TMDL                    |
        |                       |                  WebView2 / D3
        |                       |
        |             +---------+----------+
        |             |                    |
        |          TE2-derived          Microsoft
        |          engine code          TOM / MCP
        |
  DataForge Adapter ----> deterministic QA
        |
  Fabric / Databricks adapters later
```

## Source-of-truth policy

### Local PBIP
Files + Git are source of truth.

### Connected Desktop model
Live model is source of truth until exported/saved; create snapshot before mutation.

### Fabric semantic model
Remote definition/XMLA is source of truth; explicit remote-change confirmation.

## TE2 boundary

The new engine may reuse/adapt TE2 source under MIT, but keep provenance per file.

Do not create hidden coupling from WPF controls directly to TE2 WinForms classes.

## Action system

UI invokes high-level typed actions.

Actions call semantic engine, not UI objects.

This is required for:
- CLI
- tests
- MCP exposure
- undo
- CI
- headless validation.

## Agent architecture

PbiBench must be deterministic without AI.

Later:
- MCP client to Modeling MCP / Fabric tools
- PbiBench MCP server to expose safe typed actions to Codex
- approval policy between plan and mutation.

# Power BI Engineering Bench - V4 Guided Workbench

Date: 2026-09-04

## Final architecture recommendation

Do **not** build three separate products and do **not** merge every open-source Power BI tool into one executable.

Build one primary application:

**PbiBench Guided Workbench**

It contains the semantic-model engineering capabilities we can safely own and routes specialist work to the right existing tool when that is better.

```text
Power BI Desktop / PBIP
          |
          v
+--------------------------------------------------------------+
| PbiBench Guided Workbench                                    |
|                                                              |
| TE2-derived semantic engine  |  PBIP/TMDL/PBIR workspace     |
| Typed actions / BPA          |  Git / CI / deployment plan   |
| DAX Lab light                |  Report / VizForge             |
| Senior Playbook              |  Knowledge Radar               |
| DataForge truth tests        |  Agent / MCP orchestration     |
+-----------------------+-------------------+------------------+
                        |                   |
                Open/bridge to       Open/bridge to
                        |                   |
                   DAX Studio          Power BI Desktop
              timings/query plans    render/reload/publish
                        |
                  optional VS Code
              PBIP/TMDL/PBIR coding
```

## Why this is the best balance

- TE2 is MIT and gives us a strong semantic-model foundation.
- DAX Studio is excellent and already solves deep query/performance analysis; keep it external and bridge to it.
- Power BI Desktop remains the rendering/report execution authority.
- PBIP/TMDL/PBIR make code-first engineering possible without recreating Power BI Desktop.
- PbiBench becomes the user's **control plane and guide**, so the user does not need to remember which tool to use.

## Product principle

The app should answer:

> "What should I do next, where should I do it, what can be automated safely, and how do I prove it worked?"

This is more valuable than reproducing every feature of every tool.

# Architecture Lock V6

## One executable mental model

```text
                         PbiBench.exe
                              |
     +------------------------+------------------------+
     |                        |                        |
 MODEL / TE2++            ENGINEERING              PLATFORM
     |                        |                        |
 TE2-derived core         PBIP/TMDL/PBIR         Fabric REST
 TOM/BPA/scripts          Git / diff / QA         Power BI REST
 relationships            DAX tests               XMLA/TOM
 calc groups/UDFs         automation              deploy/admin
     |                        |                        |
     +------------------------+------------------------+
                              |
                    specialist/process bridges
                              |
                       DAX Studio
                       Power BI Desktop
                       VS Code when useful
```

## Application core

`PbiBench.Core` owns:
- project/workspace identity,
- capabilities,
- commands,
- action plans,
- approval,
- audit,
- tool routing.

## Semantic core

`PbiBench.Semantic` owns:
- TE2-derived semantic abstractions,
- TOM,
- TMDL,
- dependencies,
- BPA,
- scripting compatibility,
- model diffs.

## Model UI

`PbiBench.ModelEditor` is the integrated Model page.

During migration it may host/adapt some TE2 WinForms UI through a controlled compatibility boundary.
The final direction is progressively modern UI, not permanent tight coupling to old forms.

## DAX boundary

PbiBench:
- common DAX editing,
- tests,
- formatting,
- query tabs later.

DAX Studio:
- Server Timings,
- deep query plans,
- specialist performance analysis,
- VPAX where better.

## Report boundary

PBIR is code.
Power BI Desktop remains the renderer.
Desktop Bridge can reload and capture screenshots.

## Transport boundary

One operation does not imply one transport.

Possible transports:
- local PBIP/TMDL/PBIR,
- Desktop XMLA,
- Fabric/Power BI XMLA,
- Fabric REST,
- Power BI REST,
- Fabric Admin REST,
- ARM capacity APIs,
- external MCP,
- DAX Studio/dscmd.

The router chooses based on operation/context/safety.

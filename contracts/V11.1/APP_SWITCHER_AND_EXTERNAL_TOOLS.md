# App switcher and external tools

## UX goal

The user should always know which tool owns a capability.

Add a compact **Apps / Tools** entry to the top command surface or left navigation. It can open a small launcher panel.

Suggested tiles:

| Tile | Type | Behavior |
|---|---|---|
| Semantic IDE | current app | current window/home |
| Fabric Toolbox | PbiBench sub-app | launch separate executable |
| DataForge | PbiBench companion | launch configured executable/project |
| AI Context Export | Semantic utility | open export dialog |
| DAX Studio | external | launch with current server/database/query when available |
| Power BI Desktop | external | launch/open project where applicable |
| VS Code | external | open PBIP/project folder |
| Provenance / About | maintenance | show component versions, source class, pins, local patches |

Use distinct icons and subtitles such as `PbiBench`, `External`, `Companion`, not a flat list that implies all code is owned by the same subsystem.

## DAX Query

PbiBench DAX Query remains inside Semantic IDE because it is part of normal semantic-model development.

DAX Studio remains an external specialist tool. The DAX workspace should expose a visible **Analyze in DAX Studio** handoff.

Do not create a second DAX Query executable unless a future architecture constraint requires it.

## Missing tools

Never hard-fail. Show:
- Installed + version if detectable
- Not installed
- Configure path
- Open website/documentation only when user explicitly asks/uses the link


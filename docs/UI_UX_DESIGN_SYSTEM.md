# UI / UX design system

## Direction

The interface should combine:
- Tabular Editor's engineering density,
- Power BI's object familiarity,
- a newsroom/editorial visual hierarchy,
- modern Windows interaction patterns.

Do not reproduce The Economist or BBC brand identity. Build an original editorial system.

## UI technology

Preferred:
- WPF .NET 10
- `WPF UI` (MIT) for navigation/window/control plumbing
- Microsoft Fluent UI System Icons (MIT)
- custom resource dictionaries for the PbiBench visual system
- WebView2 only where HTML/SVG/D3 is actually useful.

## Theme — Light first

Suggested original tokens:

```text
Canvas          #F7F7F3
Surface         #FFFFFF
SurfaceAlt      #F0F2F3
Ink             #111315
InkMuted        #65717A
Navy            #0B3046
Navy2           #154A64
EditorialAccent #D4493F
PowerBIAccent   #F2C811
Success         #28734A
Warning         #AA6C16
Danger          #B93131
Border          #D7DDE1
CodeBackground  #F3F5F6
```

Use Power BI yellow sparingly for Power BI-specific state, not as the whole brand.

## Typography

- Segoe UI Variable for interface
- Cascadia Code for DAX/TMDL/PBIR/JSON
- compact 12–14px data grids
- 18–24px section headings
- 28–34px page heading only when needed
- avoid giant SaaS marketing typography.

## Geometry

- 4px corner radius default
- 0–1 subtle shadow level only
- 1px separators
- dense toolbars
- high information density
- 8px base spacing grid.

## Main shell

```text
┌─────────────────────────────────────────────────────────────────────┐
│ PBI Bench | Connect | Scan | Apply | Undo | Git | QA | Deploy      │
├───────────────┬─────────────────────────────────┬───────────────────┤
│ Workspace     │ Editor / Overview / DAX / PBIR │ Inspector         │
│ Model tree    │                                 │ Findings          │
│ Report tree   │                                 │ Action params     │
│ Git tree      │                                 │ Properties        │
├───────────────┴─────────────────────────────────┴───────────────────┤
│ Output | DAX query results | validation | Git diff | agent journal  │
└─────────────────────────────────────────────────────────────────────┘
```

## Home / Standardize Model page

Use the user's PBI-Pimp screenshot only as conceptual inspiration: a fast list of high-value actions.

Create an original two-column action board:

```text
MODEL FOUNDATIONS                    MEASURES & QUALITY
[calendar icon] Calendar             [calculator icon] Explicit measures
[table icon]    Measure table        [code icon]       Format DAX
[clock icon]    Last refresh         [timeline icon]   Time intelligence
[scale icon]    Units                [shield icon]     BPA / Quality
```

Each row shows one status chip:
- Good
- Finding
- Preview
- Unsupported

Clicking opens Scan findings on the right.

## Navigation

Primary:
- Home
- Data
- Model
- DAX
- Report
- Visuals
- QA
- Git
- Deploy
- Agent

Secondary contextual tabs replace new windows.

## Icons

Use Fluent System Icons regular by default; filled only for current navigation/selected important state.

No emoji in the product UI.

## Keyboard / Stream Deck

Every Action has a stable command ID.

Support:
- command palette
- configurable keyboard shortcut
- JSON command-map export
- later Stream Deck profile generator.

Do not hard-code numeric macro IDs as the permanent command identity.

## Diff UX

Every mutation shows:
- object
- before
- after
- source/reason
- validation
- reversible yes/no.

For TMDL/PBIR, expose text diff and semantic diff.

## Visuals / VizForge

WebView2 panel:
- native visual preview spec
- D3/editorial preview
- accessibility status
- PBIR target/native/custom visual capability
- screenshot at Power BI report dimensions.

# UI / UX — Editorial Engineering Workbench

## Goal

A serious Power BI engineering IDE with the clarity of high-quality data journalism.

Do not imitate a newspaper website or copy Economist/BBC visual identity.

Use the principles:
- hierarchy
- restrained color
- strong typography
- annotation
- dense information without clutter
- evidence next to action.

## Shell

```text
┌──────────────────────────────────────────────────────────────────┐
│ PbiBench   Workspace   model.pbip     Git: clean    Connection ● │
├──────────────┬────────────────────────────────┬──────────────────┤
│ NAV          │ EDITOR / CANVAS                │ INSPECTOR        │
│              │                                │                  │
│ Workspace    │ DAX / TMDL / PBIR / Diagram   │ Properties       │
│ Data         │                                │ Findings         │
│ Model        │                                │ Dependencies     │
│ DAX          │                                │ Proposed change  │
│ Report       │                                │                  │
│ Automate     ├────────────────────────────────┴──────────────────┤
│ QA           │ OUTPUT: tests / queries / Git diff / audit        │
│ Git          │                                                   │
│ Deploy       │                                                   │
│ Agent        │                                                   │
└──────────────┴───────────────────────────────────────────────────┘
```

## Design tokens

Original palette, not copied branding:

- Ink: `#15232D`
- Navy: `#173C52`
- Paper: `#FAFAF7`
- Surface: `#FFFFFF`
- Border: `#D8DFE3`
- Accent red: `#C54134`
- Power BI indicator yellow: `#E7BE24`
- Success: `#2F7D5C`
- Warning: `#A86D16`
- Error: `#A93434`

Typography:
- UI: Segoe UI / system sans
- code: Cascadia Code
- optional editorial headings: a legally distributable/system serif only; do not bundle proprietary fonts.

## Icon system

Use a permissively licensed Fluent-style icon set after license verification.

Do not reuse Tabular Editor 3 proprietary icons.

## Interaction principles

### Scan before Fix
Every automation screen should make scan/dry-run the primary flow.

### Diff before Apply
Show object-level changes:
- old
- new
- reason
- validation

### Evidence beside recommendation
A BPA finding should link directly to:
- object
- rule
- potential fix
- before/after.

### Keyboard first
Command palette:
- `Ctrl+K`
- fuzzy action search
- model object search
- open DAX
- run scan
- Git commands.

### Power-user density
Allow compact rows and multi-select.

Do not imitate oversized consumer dashboards inside the engineering UI.

## Special views

### Model Diagram
D3/WebView2 with original table cards and relationship lines.

### Automation Gallery
Icon + name + context + risk + estimated changes.

### Git Semantic Diff
Group changes by:
- Model
- DAX
- Report
- Theme
- Connections
- Deployment metadata.

### DAX Lab
Editor + diagnostics + query result + metrics, all visible without modal windows.

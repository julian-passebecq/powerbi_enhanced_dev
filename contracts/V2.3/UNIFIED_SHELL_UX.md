# Unified PbiBench shell UX

## One normal entry point

The normal user starts:
`PbiBench.exe`

Do not create another Hub executable.

Report Studio and Fabric Toolbox remain child processes for runtime isolation but ship inside the same product package.

## Navigation

Use a compact left rail:

```text
PbiBench

Home
Model
DAX
Automate
Report
Project
Fabric

Tools

Settings
About
```

Do not expose every historical internal panel as a top-level item.

## Home / Start Center

Sections:

### Continue
Recent PBIP/models/reports.

### Build
- Semantic Model
- DAX Workbench
- Automation Gallery
- Report Studio

### Project
- PBIP / Git
- Validation / Recovery
- Design Exchange

### Platform
- Fabric Toolbox
- Power BI Desktop

### Specialist
- DAX Studio
- Bravo
- VS Code

Each card:
- icon
- title
- one-line description
- status chip
- one primary action

No giant dashboard of buttons.

## Project context strip

At the top of core workspaces:

```text
Contoso.pbip   main
Model: Contoso Sales
Report: Executive.Report
PBIP · TMDL · PBIR
Disk: Clean · Live: Connected
```

No credential or access-token detail.

## Module behavior

Click `Report`:
- if report known, launch/focus Report Studio on that report;
- otherwise show a compact chooser/open action.

Click `Fabric`:
- launch/focus Fabric Toolbox.

The main shell may show module status but does not host their WPF windows inside net48.

## Command palette

If clean:
`Ctrl+K`

Commands:
- Home
- Model
- DAX
- Automation Gallery
- Report Studio
- PBIP / Git
- Design Exchange
- Fabric Toolbox
- Analyze in DAX Studio
- Open in Bravo
- Open in Desktop
- Open in VS Code

Navigation only. No plugin framework.

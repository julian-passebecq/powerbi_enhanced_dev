# Master Prompt — PbiBench Pass 1.5

## Baseline is protected

The current application launches successfully and TE2 2.28 is integrated in-process.

Treat the current running screenshot as the reference baseline.

Do not:
- replace the semantic engine,
- split TE2 into another process,
- perform broad dependency upgrades,
- rewrite the working model tree/property editor,
- remove commands because they look old,
- start unrelated Fabric/PBIR/AI epics.

Any change that touches TE2 hosting must have a smoke test.

## Product goal

Current visual impression:

> PbiBench shell + embedded TE2 application.

Desired visual impression:

> PbiBench IDE with a mature semantic model editor at its core.

The central change is **integration**, not reinvention.

## 1. Product identity

Add a proper original PbiBench application identity:
- `.ico` wired into executable
- WPF window icon
- taskbar icon
- future shortcut/installer compatibility

The icon must be original, not copied from Power BI, Tabular Editor, DAX Studio, Microsoft Fabric, BBC, Economist, etc.

Use the icon brief in `ICON_AND_BRANDING.md`.

## 2. Command architecture

Create a PbiBench command surface for the most important existing TE2 operations.

Primary command bar:

```text
Open | Connect | Save
Undo | Redo
Run BPA | Automate
DAX Studio | Diagram
```

Secondary / overflow:
- advanced TE2 commands
- scripts
- perspectives
- translations
- specialized legacy tools

Important:
- do not hide a TE2 command until the equivalent PbiBench command is verified;
- keep a temporary "Advanced / Legacy TE2" menu during migration;
- centralize command execution so keyboard shortcuts, toolbar buttons, command palette, and context menus call the same command implementation.

## 3. Layout

Target:

```text
┌─────────────────────────────────────────────────────────────────────┐
│ PbiBench | Model / Project | Connection | Git | Validation status  │
├──────────────┬──────────────────────────────────┬───────────────────┤
│ NAVIGATION   │ MODEL WORKSPACE                  │ INSPECTOR         │
│              │                                  │                   │
│ Home         │ tree + editor + properties       │ selection         │
│ Model        │ or diagram / BPA / automation    │ dependencies      │
│ DAX          │                                  │ findings          │
│ Automate     │                                  │ actions           │
│ Diagram      │                                  │                   │
│ PBIP/Git     │                                  │                   │
│ QA           ├──────────────────────────────────┴───────────────────┤
│ ...          │ OUTPUT / VALIDATION / TESTS / LOG                    │
└──────────────┴──────────────────────────────────────────────────────┘
```

The screenshot currently reserves too much permanent empty workspace.

Use:
- tabs
- collapsible panes
- persisted splitters
- context-sensitive bottom output
- context-sensitive inspector

## 4. Selection-aware inspector

The inspector must become one of PbiBench's core differentiators.

### Measure example

Show:
- name
- table
- expression
- format
- display folder
- description
- dependencies count
- BPA findings count
- test status

Actions:
- Edit DAX
- Format
- Dependencies
- Generate test
- Analyze in DAX Studio

### Column example

Show:
- name/table
- data type
- hidden
- SummarizeBy
- key/source metadata if available
- cardinality later
- references/usages
- BPA findings

Actions:
- Find usages
- Best practices
- Preview safe fixes

### Relationship example

Show:
- From / To
- cardinality
- active/inactive
- cross-filter direction
- security filtering behavior where applicable

Actions:
- Go to related tables
- Show on diagram
- Review best practices

Do not overload the inspector with every raw TOM property. Keep raw property editing available in the model editor.

## 5. Automation Gallery

Make `Automate` a real product page.

Initial visible actions:
- Explicit Measures
- Format Measures
- Measure Table
- Calendar Table
- Time Intelligence
- Units / Dynamic Format
- Last Refresh
- SummarizeBy=None
- Organize Display Folders
- Add Descriptions
- BPA Safe Fixes

Every action follows:

```text
Scan
 -> Findings
 -> Preview exact changes
 -> Apply
 -> Validate
 -> Undo / Accept
```

Show:
- scope
- number of affected objects
- risk class
- before / after
- validation status

Never make these opaque one-click scripts.

## 6. BPA+

Convert BPA from "issue count" into actionable engineering review.

Finding UI:
- severity
- object
- rule
- explanation
- current value
- proposed value
- fix classification
- Go to object
- Preview fix
- Ignore/suppress where appropriate

Fix classes:
- SAFE
- REVIEW
- BENCHMARK REQUIRED
- MANUAL ONLY

Do not auto-apply performance-sensitive recommendations.

## 7. DAX Studio handoff

Keep DAX Studio standalone.

Polish:
- visible detected path in Settings / Specialist tools
- right-click measure -> Analyze in DAX Studio
- active expression -> temp `.dax`
- pass model server/database/file
- clear error if executable missing
- settings override for executable path

Do not merge DAX Studio source/UI.

## 8. Model Diagram

Improve the existing Model Diagram toward:
- fact / dimension distinction
- active / inactive relationships
- cardinality labels
- filter direction
- table counts / measure counts
- click -> select same model object
- zoom / pan
- fit
- search
- automatic layout

Later overlays are not part of this pass.

## 9. PBIP / Git

When a PBIP project exists, show:
- project root
- semantic model folder
- TMDL detected
- PBIR detected
- current branch
- dirty / clean
- changed files grouped by model/report
- path warnings
- unapplied changes warning

Do not build a generic Git client.

## 10. Home

Home should be task-driven:

```text
Open PBIP project
Connect to Power BI Desktop
Open semantic model file

Improve this model
Run best practices
Create common measures
Analyze performance
Review Git changes
```

Show recent projects / models below.

## 11. Visual polish

Keep the current light workbench direction.

Improve:
- consistent spacing
- command sizes
- icons
- selected navigation
- typography hierarchy
- border/separator consistency
- empty states
- hover/focus states
- DPI behavior

Do not make it flashy.
Do not add card-heavy dashboard styling.

## 12. Regression first

After every significant UI/command migration:
- launch
- demo model load
- select object
- edit property
- edit expression
- Save
- Undo/Redo
- BPA
- Automation preview/apply/undo
- DAX Studio launch
- Model Diagram
- PBIP/Git
- close/reopen

The current launch bug was already fixed. Do not reintroduce dependency/runtime instability while polishing UI.

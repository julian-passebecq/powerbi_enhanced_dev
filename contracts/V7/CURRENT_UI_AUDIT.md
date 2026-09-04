# Current UI Audit

Reference screenshot: `reference/current_pbibench_working_baseline.png`

## Strong foundation

The baseline already proves:
- real integrated TE2 editor
- one PbiBench shell
- model loads
- session summary
- model object counts
- undo state
- DAX Studio detection
- output / validation panel
- navigation for future modules
- TE2 2.28 baseline visible

These are not placeholders anymore.

## Main usability issues

### 1. Competing chrome
There is:
- PbiBench top toolbar
- TE2 menu
- TE2 icon toolbar

This makes the center feel embedded rather than integrated.

### 2. Legacy icon density
TE2's tiny toolbar icons conflict with the larger PbiBench controls.

### 3. Excess empty space
Large lower editor areas are empty in the default selection state.

### 4. Inspector is underused
The right pane mostly shows session/static information.
It should become selection-aware.

### 5. Weak action hierarchy
Open, Save, Undo, Demo model, DAX Studio are visually similar despite different importance.

### 6. Missing app identity
Window/taskbar icon missing.

### 7. Navigation is structurally good but visually basic
Keep the structure; improve selected state, iconography, spacing, and context.

## Recommended direction

Do not throw away the current UI.

Iterate:
1. identity/icon
2. command consolidation
3. context-sensitive inspector
4. Automation/BPA workflows
5. layout persistence
6. diagram polish
7. PBIP/Git UX

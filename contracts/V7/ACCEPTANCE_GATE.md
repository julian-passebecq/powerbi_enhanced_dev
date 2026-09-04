# Pass 1.5 Acceptance Gate

Stop at this gate.

## Must be true

### Identity
- PbiBench icon appears in window chrome
- PbiBench icon appears correctly in taskbar

### Reliability
- clean launch
- no System.Memory / assembly mismatch regression
- current TE2 integrated model load still works

### Model
- tree
- selection
- properties
- expression edit
- Save
- Undo/Redo
- BPA
- C# script entry
remain operational

### Integration
- primary PbiBench command bar owns Open/Connect/Save/Undo/Redo/BPA/Automate/DAX Studio/Diagram
- duplicated TE2 chrome reduced without feature loss
- legacy/advanced commands remain accessible

### Inspector
- meaningful output for at least:
  - table
  - column
  - measure
  - relationship

### Automation
- gallery visible
- preview works
- apply/undo works for existing safe actions

### BPA
- detailed finding view
- object navigation
- preview-first fix

### DAX Studio
- detected/override path
- same server/database/file passed
- meaningful missing-app error

### Diagram
- zoom/pan/fit
- cardinality/direction
- active/inactive
- click-to-select

### PBIP/Git
- meaningful state for a fixture or real PBIP project

### UX
- 100%
- 125%
- 150%
Windows scaling checked

## Deliverable report

Codex should report:
1. screenshots of Home / Model / Automate / BPA / Diagram / PBIP-Git
2. build result
3. test result
4. remaining legacy TE2 UI
5. any known regressions
6. next recommended Pass 2 items

Do not continue into Fabric/PBIR/Agent without explicit approval.

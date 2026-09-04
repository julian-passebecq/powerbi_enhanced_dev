# Test and Acceptance V6

## Pass 1 acceptance checklist

### Build
- solution builds on intended Windows/.NET environment,
- bundled/upstream TE2 baseline recorded,
- no unreviewed license file removed.

### Semantic editor
- connect/open a model,
- model tree renders,
- select/edit property,
- edit measure,
- relationship/object changes still work,
- dependency view works,
- undo/redo works,
- BPA works,
- scripting entry remains available.

### Automation
- five or more actions show dry-run,
- exact affected objects displayed,
- three safe actions apply,
- undo restores prior state,
- unsupported contexts fail safely.

### DAX Studio
- generated launch command is tested,
- correct server/database passed,
- current `.dax` file opens,
- missing DAX Studio yields actionable message.

### Diagram
- relationship graph renders,
- active/inactive visible,
- relationship direction/cardinality visible,
- selecting a table syncs to model editor.

### Workspace/Git
- PBIP detected,
- Git status detected,
- warnings for risky project state are visible.

### UX
- app is usable at 100% and 150% scaling,
- keyboard navigation exists for primary actions,
- no destructive operation is hidden behind a generic "Fix" button.

## Regression rule

Any TE2 behavior we intentionally preserve must receive a characterization/regression test before deep refactoring.

## Mutation rule

All newly introduced bulk model operations follow:

`scan -> findings -> preview -> apply -> validate -> undo/accept`

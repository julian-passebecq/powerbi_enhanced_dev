# Tool Router / Guided Workflow

This is a core PbiBench differentiator.

The user should never have to remember whether a task belongs in Tabular Editor, DAX Studio, Power BI Desktop, VS Code or Fabric.

## Router examples

| Intent | Preferred route |
|---|---|
| Add/format/reorganize measures | PbiBench semantic engine |
| Create calculation group | PbiBench typed action / semantic engine |
| Run BPA and safe fixes | PbiBench |
| Edit TMDL at object level | PbiBench; optionally open VS Code |
| Write DAX query | PbiBench DAX Lab light |
| Need Server Timings/query plan | Open DAX Studio with current model + generated `.dax` file |
| Create VPAX | DAX Studio/VertiPaq adapter |
| Build/inspect report visuals | Power BI Desktop + PbiBench PBIR/VizForge |
| Bulk visual/report metadata changes | PbiBench PBIR engine |
| Validate report render | Desktop Bridge / Power BI Desktop |
| Git diff/branch/commit | PbiBench Git |
| Deep raw text merge conflict | Open VS Code/Git client |
| AI model bulk edit | PbiBench Agent -> Modeling MCP with plan/approval |
| Fabric deployment | PbiBench deployment planner/API adapter |

## Guided task card

Every recommendation card should display:

```text
Task: Investigate slow DirectQuery page

1. Capture visual query from Performance Analyzer
2. Open query in DAX Studio
3. Enable Server Timings
4. Inspect SE dependency/timeline
5. PbiBench: compare MaxParallelismPerQuery scenarios
6. Check Maximum connections per data source
7. Re-run benchmark
8. Save findings to project Knowledge/Performance note
```

Buttons:
- `Do in PbiBench`
- `Open DAX Studio`
- `Open Power BI Desktop`
- `Open VS Code`
- `Save as project checklist`

## Context awareness

Router input:
- connection type
- PBIX/PBIP
- storage mode
- compatibility level
- report/model ownership
- Git state
- local/open Desktop instance
- Fabric capacity/workspace
- installed tools
- model/report findings.

The router must explain **why** it chose a tool.

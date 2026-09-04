# AGENTS.md — PbiBench V6 implementation contract

## Mission

Build **PbiBench**, a single Windows-first Power BI/Fabric engineering IDE.

The first useful product is **PbiBench + integrated TE2++ Model Editor**.

## Architecture decisions CLOSED

1. **PbiBench.exe is the main application.**
2. **TE2-derived code is the semantic-model engine and Model Editor foundation inside PbiBench.**
3. The final product is not two separate products named PbiBench and TE2++.
4. Preserve mature TE2 behavior where it is valuable; modernize incrementally.
5. Do not spend Pass 1 rewriting every TE2 WinForms control.
6. DAX Studio remains a separate executable/process; integrate it deeply.
7. Power BI Desktop remains the final Desktop renderer/author where necessary.
8. PBIP/TMDL/PBIR are first-class code artifacts.
9. Fabric REST, Power BI REST and XMLA/TOM are first-class transports later.
10. Modeling MCP/Desktop Bridge are optional first-party preview transports, not sole dependencies.
11. No binary PBIX reverse engineering.
12. No TE3 code/assets/decompilation. Implement public capability categories independently.
13. PbiBench must remain useful without an LLM.

## First-pass philosophy

**Visible useful product first, architectural cleanup second.**

Do not make the user wait until a perfect TE2 modernization is complete before they can use the editor.

Allowed Pass 1 strategy:
- retain/adapt working TE2 WinForms controls where that is the fastest safe route,
- place them behind PbiBench service boundaries where practical,
- progressively replace them in later passes.

## First Pass required product

The user must be able to:

1. Start PbiBench.
2. Open/connect a semantic model using the supported TE2 path.
3. See the TE2-derived model tree and property editing workflow.
4. Create/edit common semantic-model objects without losing existing TE2 behavior.
5. Run BPA.
6. Open the Automation panel.
7. Preview at least these safe actions:
   - format measure expressions,
   - create explicit SUM measures from selected numeric columns,
   - create/use a measure table,
   - set selected columns `SummarizeBy = None`,
   - organize measures into display folders.
8. Apply an action only after showing the affected objects.
9. Format DAX.
10. Open the active expression/query in standalone DAX Studio.
11. Display a basic relationship/model diagram.
12. Show current connection + PBIP/Git status when a project is available.
13. Undo/rollback local changes where the underlying TE2 model path supports it.
14. Build and run tests.

## Do NOT block Pass 1 on

- full Fabric tenant administration,
- complete PBIR authoring,
- complete DAX Lab,
- AI/MCP,
- DataForge,
- custom visuals,
- full TE3 feature parity,
- full DAX debugger,
- replacing every legacy TE2 UI component.

## TE2 source

Bundled offline snapshot:
`vendor/TabularEditor2-bundled/`

Current web baseline for this handoff:
- TE2 is MIT/open source.
- GitHub current release reported as 2.28.0, released March 2, 2026.
- TE2 source has additional third-party licenses that must be preserved/reviewed.

If network access exists:
- fetch and pin TE2 2.28.0 before major modifications.
If network access is unavailable:
- build the bundled snapshot first and proceed.

Never delete upstream license files.

## DAX Studio boundary

Do not embed DAX Studio UI/source into PbiBench core.

Implement:
- discovery/configuration of `daxstudio.exe`,
- launch with `--server`, `--database`, `--file`,
- later optional `dscmd` automation for queries/benchmarks/VPAX.

Routine DAX belongs inside PbiBench.
Deep Server Timings/query-plan work routes to DAX Studio.

## Code standards

- Keep UI separate from service/adapters.
- CancellationToken on public async I/O.
- No secrets/tokens in logs.
- Remote writes require an ApprovedChangePlan.
- Local bulk edits require preview + undo/snapshot.
- Tests before significant TE2 behavior changes.
- Preserve source licenses/attribution.
- Keep external/process adapters replaceable.
- No static global service locator.

## Pass 1 acceptance gate

Pass 1 is complete only if:
- app builds,
- app launches,
- a semantic model can be opened/connected,
- TE2-derived editing works,
- five Automation actions preview correctly,
- at least three safe actions apply correctly,
- BPA works,
- DAX Studio bridge launches correct context,
- model diagram renders,
- PBIP/Git status appears when applicable,
- automated tests pass,
- no destructive action bypasses preview/approval.

Stop and report at this gate before starting the cloud/report/AI epics.

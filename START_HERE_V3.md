# Power BI Engineering Bench — V3 Codex handoff

Date: 2026-09-04

This V3 extends the TE2-based V2 with four major workstreams:

1. **DAX Lab / DAX Studio-style engineering**
2. **Visual Calculations + Calculation Placement Advisor**
3. **SQLBI Knowledge Radar / test-pattern catalog**
4. **VizForge Custom Visual Studio**

## Architecture remains

```text
TE2 MIT semantic foundation
    + modern .NET workbench
    + PBIP/TMDL/PBIR/Git
    + typed bulk actions
    + DAX query/test/performance lab
    + visual calculations
    + Modeling MCP / report authoring skills
    + DataForge truth tests
    + VizForge native/custom visuals
    + Fabric / Databricks scenario planner
```

## New hard rule: choose where a calculation belongs

Do not default every analytical request to a measure.

PbiBench must decide between:

- Power Query custom column
- calculated column/table
- semantic model measure
- DAX UDF
- calculation group
- visual calculation

and explain the choice.

## New hard rule: SQLBI is a knowledge source, not a codebase

SQLBI content is copyrighted.

PbiBench may:
- index public metadata/titles/URLs/topics,
- show short user-authored summaries,
- open the original article,
- attach local sample files that the user lawfully downloaded,
- turn concepts into original rules/tests/actions with citations.

Do not scrape/copy entire SQLBI articles into the product.

## New hard rule: custom visual studio is original

PBIVizEdit is a useful product benchmark only.

Build our own:
- VizSpec
- visual primitive library
- data-role mapper
- format-property editor
- D3 preview
- native PBIR mapper
- custom `pbiviz` generator/package pipeline

using the Microsoft MIT visual toolchain and original UI/UX.

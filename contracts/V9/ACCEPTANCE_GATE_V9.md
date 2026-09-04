# V9 Acceptance Gate

V9 is accepted when the following are demonstrated.

## DAX IDE
- autocomplete uses live model metadata
- diagnostics work
- Go/Peek definition works
- UDF-aware completion works
- query tabs + partial execution work
- multiple result grids work
- safe code actions preview diffs
- DAX Studio deep-analysis handoff still works.

## Data
- multiple table preview tabs
- paging/fallback behavior correct
- profile data works
- relationship coverage works
- Pivot Lab can test measures.

## Model Authoring
- UDF Workbench
- Calendar Editor
- Perspective Editor
- Translation Editor
- Table Groups
- Diagram+ selection/edit flow
- DAX Scripts multi-object diff/apply.

## Automation
- Safe C# Script Preview demonstrates before/after model diff
- Trusted Legacy mode is clearly separate
- action recorder generates reusable action recipe
- built-in BPA rules visible and versioned.

## Quality
- VertiPaq analysis integrated
- optimization cockpit
- semantic test artifacts
- long operations background/cancellable.

## Fabric
- Fabric browser
- import wizard
- Direct Lake workflow
- source schema compare
- Fabric preview.

## Workspace
- Disk / Live / Git state visible
- safe compare/push/pull
- conflict handling
- semantic Git diff.

## CLI
At least:
- inspect
- bpa
- query
- validate
- diff
support JSON + noninteractive execution.

## Legal/source
- all reused source/license origins documented
- no proprietary TE3 implementation copied
- original PbiBench UX retained.

## End report

Show:
- screenshots
- build/tests
- new capabilities
- performance numbers for large model/editor scenarios
- open limitations
- next optional features: full DAX debugger, semantic compiler, package manager, localization.

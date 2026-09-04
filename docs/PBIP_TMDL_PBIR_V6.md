# PBIP / TMDL / PBIR integration V6

## Current status to surface in UI

As of this handoff:
- PBIP saving in Power BI Desktop remains Preview.
- enhanced PBIR remains Preview.
- Desktop Bridge remains Preview.
- TMDL is a first-class semantic-model text representation and TMDL View is broadly available; TMDL View in service has preview aspects.

Do not hide preview status.

## Model workflow

```text
PBIP / TMDL on disk
      |
      +-> PbiBench.Semantic
      |    inventory
      |    diff
      |    BPA
      |    actions
      |
      +-> Git
      |
      +-> Desktop reload when needed
```

## Live workflow

```text
Power BI Desktop / Fabric semantic model
      |
      +-> XMLA/TOM
      |
      +-> PbiBench Model Editor
```

Later compare:
- live model
- disk TMDL
- cloud definition.

## Report workflow

Enhanced PBIR:
- public JSON schemas,
- separate page/visual/bookmark definitions,
- clean-room PbiBench authoring later,
- validate structurally,
- reload/render in Desktop,
- screenshot QA.

Do not reverse-engineer PBIX.

## Safety

Before external PBIP edits:
- detect path length risk,
- check Git status,
- detect/flag `unappliedChanges.json`,
- baseline,
- modify,
- validate,
- reload Desktop,
- review diff.

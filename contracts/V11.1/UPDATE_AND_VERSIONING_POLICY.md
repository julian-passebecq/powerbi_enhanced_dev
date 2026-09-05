# Update and versioning policy

## Purpose

Avoid losing track of what changed when TE2, Microsoft Fabric packages/APIs, DAX syntax, or companion tools evolve.

## Update lanes

### Lane 1 - TE2 foundation

Owned files:
- `vendor/TabularEditor2-2.28.0/`
- `vendor/patches/te2-*`
- `PbiBench.ModelEditor`
- native TE2 boundary tests

Procedure:
1. fetch candidate TE2 version/commit;
2. inspect license/release changes;
3. rebuild clean upstream;
4. rebase/apply local patches explicitly;
5. run native model/editor/undo/script regressions;
6. package in isolated branch;
7. update provenance pin only after green gate.

Do not update TE2 as a side effect of a Fabric feature.

### Lane 2 - Fabric

Owned files:
- `PbiBench.Fabric`
- `PbiBench.FabricToolbox`
- Fabric transport/auth tests

MSAL, SqlClient and Fabric API changes are handled here. Semantic IDE consumes stable contracts/adapters.

Do not update Fabric dependencies as part of a TE2 upgrade unless required and documented.

### Lane 3 - DAX language/editor

Owned files:
- `PbiBench.Dax.LanguageService`
- DAX editor/query integration

Refresh public syntax/function metadata independently. Keep engine validation authoritative.

### Lane 4 - External tools

DAX Studio/Power BI Desktop/VS Code are launch/handoff integrations. Detect supported versions where useful; do not vendor or fork them without a separate explicit decision.

### Lane 5 - PbiBench-owned features

Automation, Data Exploration, QA, workspace logic, AI export, CLI, etc. evolve under PbiBench versioning.

## Component version manifest

Add a generated/readable runtime manifest, for example:

```json
{
  "productVersion": "11.0.0-dev",
  "semanticIde": "11.0.0-dev",
  "fabricToolbox": "0.1.0",
  "contracts": 1,
  "te2": {
    "version": "2.28.0",
    "pin": "75f10e331b8de0dda5c213180b9b8867b4a38191",
    "patches": [
      "remote-write-review",
      "function-undo-order"
    ]
  },
  "externalTools": {
    "daxStudio": "detected-at-runtime"
  }
}
```

The About/Provenance UI should display this rather than requiring source inspection.

## Release naming

Avoid labels such as `v3` or `newv` for future major pushes.

Use product/area labels:
- `v11.0 - Compartmentalized Platform`
- `fabric-toolbox-v0.1`
- `semantic-ide-v11.1`

A top-level release may record exact component versions.


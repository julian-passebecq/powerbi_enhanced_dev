# Workspace / DevOps Specification

## Dual-State Workspace

PbiBench should improve on simple "workspace mode" by making state explicit.

Sources:
- DISK: PBIP/TMDL
- LIVE: Desktop/XMLA/Fabric model
- GIT: repository baseline

UI:
```text
Disk   ● modified
Live   ● connected
Git    ● 4 semantic changes

[Compare] [Pull Live -> Disk] [Push Disk -> Live]
```

## State engine

Track:
- baseline semantic snapshot
- disk change sequence
- live change sequence
- file watcher events
- unsaved model edits
- external edits
- conflicts.

Never silently overwrite divergent live/disk edits.

## UDF Git layout

Adopt/test per-file UDF serialization where available.
Show UDF object diffs.

## Queries

Save DAX Query documents in PBIP project conventions where available.

## PbiBench CLI

Own command line:
```text
pbibench inspect
pbibench list
pbibench get
pbibench set
pbibench script
pbibench action
pbibench bpa
pbibench query
pbibench test
pbibench refresh
pbibench validate
pbibench diff
pbibench deploy
```

Global:
- `--json`
- `--non-interactive`
- `--profile`
- predictable exit codes.

This CLI uses the same core services as the GUI.

## CI

PR:
- load/parse
- TMDL round trip
- BPA
- DAX assertions
- schema check
- semantic diff artifact.

Deployment:
- explicit target
- preview plan
- environment credentials from secure store
- deploy
- refresh
- smoke tests.

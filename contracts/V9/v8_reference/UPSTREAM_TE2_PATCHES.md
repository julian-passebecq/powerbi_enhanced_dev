# Current TE2 upstream candidates

Do not blindly cherry-pick an open PR.
Inspect diff, run upstream tests, then integrate or independently reproduce the fix.

## PR #1343 — UDF JSON serialization
High priority.

Purpose:
- serialize each DAX UDF to its own file in folder serialization;
- reduce Git merge conflicts;
- preserve legacy opt-in behavior;
- normalize calculation-item serialization tag compatibility between TE2 and TE3.

PbiBench value:
- first-class UDF Git diffs;
- object-aware semantic diff;
- cleaner parallel development.

Action:
- evaluate PR against the pinned TE2 2.28 baseline;
- add PbiBench regression tests for:
  - round trip
  - rename
  - delete/stale file pruning
  - opt-out
  - old annotations
  - TE2 folder layout compatibility.

## PR #1341 — DAX Formatter API endpoint
High priority.

Purpose:
- use current `api.daxformatter.com` endpoint;
- remove old redirect/connection priming logic.

PbiBench value:
- formatter reliability.

Important:
PbiBench should still keep formatting behind an adapter and disclose external-service use.

## PR #1334 — Selected scripting API symmetry
High priority for Automation.

Adds missing typed singular/plural `Selected` accessors.

PbiBench value:
- cleaner script compatibility layer;
- easier conversion of legacy scripts to typed actions;
- better macro IntelliSense.

If integrated, add tests for zero/one/multiple selection behavior.

## PR #1333 — external role member fix
Correctness priority.

One-line bug fix around `AddExternalMember`.

Include if relevant to PbiBench role/RLS management and test it.

## PR #1328 — database list sorting
UX priority.

PbiBench should go further:
- sort
- search
- filter by compatibility/server/workspace
- recent/favorite semantic models.

Do not reproduce the old dialog if our Connection Hub already supersedes it.

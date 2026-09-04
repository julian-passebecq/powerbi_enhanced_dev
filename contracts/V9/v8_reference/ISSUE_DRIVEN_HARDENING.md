# Issue-driven hardening

Use the current TE2 issue tracker as a regression backlog.

## A. Connection / launch resilience
Cases to harden:
- live connected report errors should be surfaced, not crash/open broken;
- XMLA dataset loading regressions;
- Windows 11 connection issues;
- missing/dependency assembly failures.

PbiBench:
- structured connection diagnostics
- retry only when safe
- compatibility/version diagnostics
- clear "open offline / reconnect / inspect details" paths.

## B. TMDL correctness
Regression fixtures for:
- leading spaces in descriptions
- relationship serialization on save-to-folder
- malformed/empty TMDL collections
- UDF import/serialization
- calculation-item folder serialization
- `ChangedProperties` metadata where required.

PbiBench should treat TMDL round-trip fidelity as a release blocker.

## C. BPA consistency
Test same model through:
- live Desktop/XMLA
- model.bim
- TMDL/folder

BPA results should be explainably equivalent or differences should be surfaced.

## D. Long-running scripts
Current TE2 users ask for background execution.

PbiBench should implement:
- task queue
- progress
- cancellation
- captured output
- object/change plan locking
- UI remains responsive
- undo/rollback when action supports it.

Do not run arbitrary model mutation concurrently.

## E. C# compile diagnostics
Improve script errors:
- selectable/copyable diagnostics
- file/line/column
- copy all
- open offending line
- clear distinction between compile/runtime/model errors.

## F. DAX Formatter + UDF syntax
Add regression fixtures for:
- lambda/UDF parameter type hints
- defaults
- optional parameters
- function expressions

Formatter changes must never silently make DAX invalid.

## G. Security roles
New role defaults and external member creation need explicit tests.

Avoid hidden defaults that could result in unintended permissions.

# Automation / AI Specification

## C# Script Preview

Legacy TE2 scripts are powerful but opaque.

PbiBench offers two modes.

### Safe Preview mode
- clone/serialize model metadata to an isolated in-memory model
- execute only approved model-edit API surface
- diff before/after
- show changed objects/properties
- apply change plan to real model after approval.

Block filesystem/network/process access in Safe Preview.

### Trusted Legacy mode
For existing unrestricted scripts:
- explicit Trusted badge
- warn that file/network/process side effects cannot be previewed reliably
- model snapshot before run
- capture output/errors
- allow undo for model changes where possible.

## Action Recorder

Record supported model operations:
- property edits
- create/delete object
- rename
- folder changes
- measure changes

Output:
- typed PbiBench Action Recipe
- optional generated C# snippet for compatibility.

Do not serialize arbitrary UI gestures.

## Built-in BPA

Ship independently authored PbiBench rule packs:
- Naming
- Formatting
- Modeling
- Performance
- Security
- DAX
- Direct Lake
- PBIP/Git

Rule fix safety:
- SAFE
- REVIEW
- BENCHMARK
- MANUAL.

## AI Assistant

The assistant is action-oriented, not a chat toy.

Context:
- selected object
- semantic model inventory
- BPA findings
- current DAX
- PBIP/Git diff
- test results
- connection capabilities.

Allowed outputs:
- explanation
- DAX proposal
- test proposal
- typed Action Plan
- query
- model review.

No direct mutation from generated text.

Flow:
AI proposal -> parsed typed plan -> PbiBench validation -> preview -> user approve -> apply -> test.

Provider abstraction:
- OpenAI
- other provider later
- disabled/offline mode.

## DAX Package Manager

Optional later feature:
- browse open DAXLib-compatible package feed
- package metadata/version
- install/update/remove
- dependency review
- local lock file
- Git diff
- UDF compatibility-level check.

Do not use proprietary TE3 package-manager code.

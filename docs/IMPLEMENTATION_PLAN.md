# Implementation plan for Codex

## Milestone 0 — Reference build

- unpack TE2 under `vendor/TabularEditor2`
- preserve licenses
- build TE2
- run tests
- record exact toolchain and blockers
- produce `TE2_BASELINE.md`

## Milestone 1 — New shell

- create .NET 10 WPF solution
- use WPF UI package
- create editorial resource dictionaries
- Fluent icon wrapper
- main shell + panes
- command routing
- settings

No semantic writes.

## Milestone 2 — Tabular engine

- port/wrap BPA
- port/wrap TOMWrapper
- model inventory DTOs
- selection/context abstraction
- model diff representation
- safe transaction abstraction

## Milestone 3 — Action Catalog

Implement Scan for all first 8 actions.
Implement Apply + Undo for at least:
- SummarizeBy hygiene
- explicit measures
- measure table
- measure formatting

Then calendar/calc groups/last refresh.

## Milestone 4 — PBIP/TMDL/Git

- open PBIP folder
- detect TMDL
- model inventory
- Git clean-tree check
- baseline snapshot
- semantic diff
- apply/validate
- restore

## Milestone 5 — Desktop external tool

- generate `.pbitool.json`
- launch from Power BI Desktop with server/database arguments
- Desktop model discovery
- read-only first
- write gate.

## Milestone 6 — DAX workspace

- code editor
- format
- model references/autocomplete later
- execute DAX query
- test cases
- query timings
- dependency list.

## Milestone 7 — PBIR report engine

- clean-room public-schema implementation
- page/visual/bookmark tree
- JSON validation
- semantic binding resolver
- report fixer actions
- Desktop reload/screenshot.

## Milestone 8 — VizForge

- WebView2 host
- neutral VizSpec
- D3 renderer
- original themes
- native PBIR mapper
- custom visual generator using PowerBI-visuals-tools.

## Milestone 9 — Fabric / remote

- optional Semantic Link Labs/PBI Fixer concepts
- Fabric definitions/APIs
- deployment/Git integration
- Direct Lake/VertiPaq tools
- Databricks source planning.

## Milestone 10 — Agent

- MCP client/server
- high-level safe tools only
- plan -> approval -> apply
- audit journal.

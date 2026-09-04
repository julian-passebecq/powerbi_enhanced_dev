# Power BI Engineering Bench — Codex Takeover Pack V5

Date: 2026-09-04

**Architecture is now locked. Do not redesign the product before completing the first vertical slices.**

PbiBench is a Windows-first C#/.NET Power BI + Microsoft Fabric engineering control plane.

It combines:
- a modernized semantic model engine derived from MIT-licensed Tabular Editor 2,
- PBIP/TMDL/PBIR workspace engineering,
- Power BI/Fabric REST control-plane management,
- XMLA/TOM live semantic-model management,
- DAX Lab Light + a deep DAX Studio bridge,
- typed bulk actions / senior playbook checks,
- Git/CI/CD,
- Fabric estate/admin/monitoring surfaces,
- DataForge deterministic truth tests,
- SQLBI Knowledge Radar,
- VizForge custom/native visual authoring,
- AI/MCP orchestration with strict approval gates.

## Core operating model

```text
                         PbiBench
                            |
     +----------------------+----------------------+
     |                      |                      |
 LOCAL / SOURCE         LIVE MODEL            CLOUD CONTROL
     |                      |                      |
 PBIP/TMDL/PBIR         XMLA / TOM          Fabric REST API
 Git                    Desktop AS          Power BI REST API
 Desktop Bridge         Fabric XMLA         Fabric Admin API
     |                      |                Fabric Core MCP
     +----------------------+----------------------+
                            |
                         QA / PLAN
                            |
               +------------+------------+
               |                         |
          internal tools             specialists
               |                         |
      BPA / Actions / Git       DAX Studio / Desktop
      DAX tests / PBIR QA       VS Code when useful
```

## First reading order for Codex

1. `AGENTS.md`
2. `docs/CODEX_MASTER_PROMPT_V5.md`
3. `docs/ARCHITECTURE_LOCK_V5.md`
4. `docs/FABRIC_CONTROL_PLANE_V5.md`
5. `docs/SEMANTIC_ENDPOINT_ROUTER_V5.md`
6. `docs/DAX_STUDIO_BOUNDARY_V5.md`
7. `docs/TASK_BACKLOG_V5.md`
8. `docs/SECURITY_AUTH_AND_APPROVAL_V5.md`
9. `docs/API_AND_FEATURE_STATUS_V5.md`
10. V4/V3 docs for specialist details.

Then inspect `src/`, `tests/`, `configs/`, `examples/`, and `references/`.

## Build order

Do not start with AI or report generation.

```text
M0 baseline/build
 -> M1 workspace + connection hub
 -> M2 read-only Fabric/Power BI inventory
 -> M3 semantic model inventory + TE2/BPA
 -> M4 DAX Studio bridge
 -> M5 typed actions + safe write transactions
 -> M6 PBIP/Git + cloud definition pull/push
 -> M7 PBIR/report QA
 -> M8 Fabric admin/estate
 -> M9 AI/MCP
 -> M10 VizForge/DataForge full loop
```

## Non-negotiable write sequence

```text
inspect -> plan -> diff -> snapshot -> approve -> apply -> validate -> verify -> Git diff -> accept/rollback
```

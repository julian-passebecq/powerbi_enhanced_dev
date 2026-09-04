# Reference and license matrix

This is an engineering inventory, not legal advice. Codex must inspect the exact archive/version before copying code.

| Resource | Observed license/status | Decision |
|---|---|---|
| Tabular Editor 2 | MIT main repo; additional third-party licenses in repo | **Include and selectively reuse**. Preserve notices. Avoid copying legacy UI dependencies until reviewed. |
| TabularEditor/Scripts | MIT | Safe candidate for script/action reference with attribution. |
| TabularEditor BestPracticeRules | Official public rules repo | Use as rule source; preserve upstream metadata/attribution. |
| PowerBI-visuals-tools | MIT | **Include/use** for custom visual generator. |
| PowerBI Developer Samples | MIT | **Include/use as API/auth examples**. |
| Semantic Link Labs | MIT | **Include/reference**, especially Fabric remote adapter concepts. |
| PBI Fixer | Current public repo says MIT | Include as reference; re-check exact snapshot license before direct copying. |
| Microsoft Fabric Toolbox | Root MIT, some subcomponents have separate licenses | Do not bundle huge archive. Use selected documentation and verify each subcomponent before copying code. |
| pbi-tools | AGPL-3.0 since 1.0 | **Do not embed/copy** into a closed/private distributable product. Treat as behavioral/reference or optional external process after policy review. |
| Alexander Korn PBI-Tools / macro collection | No obvious root license in supplied material | **Feature research only** until per-script provenance/license is confirmed. Reimplement behaviors as typed Actions. |
| powerbi-macguyver-toolbox | Custom non-commercial restrictions | **Do not copy/derive for product**. Visual inspiration only. |
| pbir.tools | Restrictive custom terms in previously supplied archive | Do not copy. Clean-room PBIR from Microsoft schemas. |
| Fabric-Demos / Fabric-Notebooks / fabric_developer_hub supplied ZIPs | No root license found in supplied snapshots | Do not copy code until verified. Use conceptual reference only. |
| Customer Churn Power BI sample | MIT in supplied archive | Optional visual/test reference. |
| HR KPI sample | GPLv2 in supplied archive | Do not copy into proprietary code. |
| highlight.js | Separate OSS project; not needed for core product | Avoid unless chosen deliberately. |

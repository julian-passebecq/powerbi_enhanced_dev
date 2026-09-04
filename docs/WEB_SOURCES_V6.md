# Web sources checked for V6

Re-check before release because previews and versions move quickly.

## Tabular Editor
- TE2 GitHub:
  https://github.com/TabularEditor/TabularEditor
- TE2 legal status:
  https://tabulareditor.com/legal
- TE2 vs TE3:
  https://tabulareditor.com/product/why-tabular-editor/tabular-editor-2-vs-tabular-editor-3
- Migration/features:
  https://docs.tabulareditor.com/en/getting-started/migrate-from-te2.html
- TE2 Scripts:
  https://github.com/TabularEditor/Scripts
- BPA rules:
  https://github.com/TabularEditor/BestPracticeRules

Research snapshot:
- TE2 is MIT/open-source.
- current GitHub release surfaced as 2.28.0, Mar 2 2026.
- repository was still maintained in 2026.
- TE2 2.27 introduced DAX UDF and Calendar awareness plus TMDL import.

## DAX Studio
- Startup parameters:
  https://daxstudio.org/docs/features/startup-parameters/
- License:
  https://daxstudio.org/docs/license/

## Power BI source formats / external editing
- PBIP:
  https://learn.microsoft.com/power-bi/developer/projects/projects-overview
- PBIR:
  https://learn.microsoft.com/power-bi/developer/embedded/projects-enhanced-report-format
- External editing / Desktop Bridge reload:
  https://learn.microsoft.com/power-bi/developer/projects/projects-external-editing
- TMDL View:
  https://learn.microsoft.com/power-bi/transform-model/desktop-tmdl-view

Research snapshot:
- PBIP save option remains Preview.
- enhanced PBIR remains Preview.
- Desktop Bridge remains Preview.
- PBIR has public JSON schemas and external-editing workflows.

## Power BI Agentic
- https://learn.microsoft.com/power-bi/developer/agentic/power-bi-agentic-overview
- https://learn.microsoft.com/power-bi/developer/agentic/power-bi-report-authoring-skill-overview

Use first-party agent tools as optional transports later; do not make PbiBench depend on them for basic semantic editing.

## Fabric semantic model definitions
- https://learn.microsoft.com/rest/api/fabric/articles/item-management/definitions/semantic-model-definition
- https://learn.microsoft.com/rest/api/fabric/semanticmodel/items/update-semantic-model-definition

TMDL/TMSL cloud definition management belongs to later Fabric passes.

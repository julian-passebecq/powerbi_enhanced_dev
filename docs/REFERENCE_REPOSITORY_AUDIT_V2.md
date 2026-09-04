# Reference repository audit V2

## Core / can directly influence implementation

### Tabular Editor 2
**Priority: Critical**
- Supplied.
- MIT.
- Use as semantic-engine foundation.
- Preserve notices and audit third-party licenses.

### Semantic Link Labs
**Priority: High**
- Supplied.
- MIT.
- Valuable reference/adapter for Fabric semantic model/report operations.
- Python/Fabric layer, not the C# core.

### Power BI Developer Samples
**Priority: High**
- Supplied.
- Microsoft MIT.
- Use to understand supported embedding/developer patterns and as test fixtures.

### Power BI Visuals Tools
**Priority: High**
- Supplied.
- Microsoft MIT.
- Use official toolchain for generated custom visuals.

### PBI Fixer
**Priority: High feature benchmark / optional adapter**
- Supplied.
- Public project states MIT.
- Strong scan/fix pattern.
- Useful ideas: report/model explorer, BPA fixes, memory, perspectives, translations, model diagram, prototypes.
- Keep Python implementation separate from core C# unless a specific module is intentionally ported under its license with attribution.

### Fabric Toolbox
**Priority: High but selective**
- Supplied.
- Root is MIT, but individual subcomponents can have separate licenses.
- Do not wholesale import 400+ MB.
- Reuse only audited components.
- DAX Performance Tuner has mixed licensing for one derived component; audit before use.

## Useful but external / licensing caution

### pbi-tools
**AGPL-3.0**
- Do not merge into proprietary/permissive PbiBench core.
- Treat as behavior/DevOps benchmark or separate optional process if licensing is acceptable.

### Power BI MacGyver Toolbox
**Custom non-commercial license**
- Do not copy into a product intended for unrestricted use.
- Use only as visual/problem inspiration.
- Build original VizForge assets/templates.

### HR Analytics sample
**GPLv2**
- Do not use as permissive product template.
- May inspect as a private reference.

### Swiggy sample / Power BI Design Vault / Power BI Pro Studio
**No clear license in supplied ZIP**
- Do not copy implementation/assets.
- At most use high-level visual/product inspiration.

### D3 Viz Generator V7
**No top-level license found in supplied ZIP**
- Treat as user's internal/private reference.
- Do not redistribute externally until provenance is clear.
- Reuse concepts through a new VizSpec if ownership is confirmed.

## Missing optional references

- Power-BI-Design-Files
- Sales_report_using_PowerBi

Not blockers.

## Modeling MCP

The local Power BI Modeling MCP is a Microsoft preview component.

Consume it as an external tool/package.
Do not reverse engineer or embed its implementation into PbiBench.
Review the current Microsoft license/EULA at use time.

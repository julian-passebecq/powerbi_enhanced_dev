# Fabric and PBI Fixer notes

PBI Fixer is a useful benchmark for the cloud/Fabric side of this product.

Concepts worth bringing into PbiBench:
- scan-only vs fix
- Model Explorer
- Report Explorer
- model BPA and report BPA
- automated safe fixers
- VertiPaq/memory analysis
- perspectives
- translations
- model diagram
- prototype/screenshot workflow
- Direct Lake analysis
- stateless callable fixer functions.

Do not turn PbiBench into a Fabric Notebook UI. PbiBench remains a Windows C# application.

Potential integration later:

```text
PbiBench local app
   |
   +-- Local TOM/TMDL/PBIR
   +-- Modeling MCP
   +-- Fabric REST/XMLA
   +-- optional Python/Semantic Link Labs bridge
```

The 406 MB Fabric Toolbox upload is intentionally not copied into this handoff. Relevant subareas to inspect in the user's original archive/public repo:
- `tools/DAXPerformanceTunerMCPServer`
- `tools/SemanticModelMCPServer`
- `tools/MicrosoftFabricMgmtMCPServer`
- `accelerators/CICD/...`
- semantic-model metadata accelerators.

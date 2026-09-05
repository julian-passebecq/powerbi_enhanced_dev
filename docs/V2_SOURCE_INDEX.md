# Gen-2 source and license record

Implementation baseline: `bbb29c3ab7adb2e7b9c04bf71b618354847e3e92`. All 12 supplied V2.1 pack files are retained in `contracts/V2.1/`. The user's Gen-2 request extends the earlier V6 report boundary.

| Source | Verified pin / contract | Use and boundary |
| --- | --- | --- |
| [Microsoft JSON schemas](https://github.com/microsoft/json-schemas/tree/83ce11373faada0d01e76264a5cceb0ba70003e6/fabric/item/report) | `83ce11373faada0d01e76264a5cceb0ba70003e6` | MIT. 98 PBIP/report JSON schema files retained byte-for-byte under `schemas/microsoft/`, with LICENSE and SHA-256 lock manifest. Embedded for offline validation. |
| [Microsoft PBIP/PBIR contract](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-report) | Checked September 5, 2026 | Public file structures, schema-aware editing, annotations and Desktop `.pbir` opening. No binary PBIX decoding. |
| [Bravo startup parser](https://github.com/sql-bi/Bravo/blob/fbc2b12950ddb4344ae71f16de8861407c32050e/src/Infrastructure/Configuration/Settings/StartupSettings.cs) | `fbc2b12950ddb4344ae71f16de8861407c32050e` | Confirms `--server`, `--database`, optional `--ppid`. Bravo remains an external MIT companion; no code, runtime, UI, query or feature-page deep link is embedded. |
| [TabularEditor/Scripts](https://github.com/TabularEditor/Scripts/tree/3430e5a474f9975a630819ccb0abeb265697364c) | `3430e5a474f9975a630819ccb0abeb265697364c` | MIT pattern/curation reference. Gallery recipes are original PbiBench implementations; no community script source copied or executed. |
| [Official C# library](https://docs.tabulareditor.com/common/CSharpScripts/) | Compatibility/curation reference | External source navigation only. Existing native commands and typed Safe Recipes are preferred. |
| [JsonSchema.Net](https://github.com/gregsdennis/json-everything) | NuGet `7.3.1` | MIT JSON Schema evaluator; modern Report Studio lane only. No network schema fetch; missing schema dependencies block writes. Package notices included by packaging. |
| TE2 | Existing `2.28.0 / 75f10e331b8de0dda5c213180b9b8867b4a38191` | Existing two integration patches, attribution and third-party notices preserved. No TE2 source changes in this pass. |

The schema registry normalizes each schema's runtime `$id` to its actual pinned file URI because several upstream files repeat sibling/version IDs. The stored source files and validation constraints remain unchanged. Unknown versions are readable but cannot be approved for mutation. Updating the schema bundle is an explicit PBIP/PBIR lane change; refresh its pin, hash manifest and regression fixtures together.

DAX Guide opens in the browser. In-app signature descriptions use PbiBench's existing original local catalog. DAX.do is conceptual inspiration only; no branding, CSS, icons, screenshots or source copied. No TE3 assets/code or non-commercial PBIR project code is included. Fabric authentication and remote operations remain in Fabric Toolbox.

## Pass 3 additions

- Neutral launch and handoff: `src/PbiBench.ExternalTools/` (original, no auth/UI/model dependency).
- Provider-neutral contracts: `src/PbiBench.DesignExchange/` (existing metadata DTO reuse).
- Microsoft report-theme schema: `schemas/report-theme.lock.json` (2.156, exact commit and SHA-256, retained MIT license).
- Original vectors and light tokens: `src/PbiBench.DesignSystem/` (adjacent MIT license/inventory).
- Product/package versions: `components.json`; exact graph: `docs/architecture/module_catalog.json`.

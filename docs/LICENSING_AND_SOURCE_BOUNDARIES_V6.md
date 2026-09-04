# Licensing and source boundaries V6

## Tabular Editor 2
- MIT/open source.
- This is the semantic foundation.
- Preserve the MIT license and third-party license files.
- The TE2 repository itself reports multiple licenses for bundled third-party components; review before porting UI/control code.

## Tabular Editor 3
- Commercial.
- Public documentation is a feature/workflow benchmark.
- No source copying, binary decompilation, asset copying, proprietary icon/theme copying, or undocumented internal replication.
- Build independent implementations from public Microsoft APIs and original UX.

## DAX Studio
- Microsoft Reciprocal License.
- Keep as external process/reference by default.
- Do not merge DAX Studio source into permissive PbiBench core.

## TabularEditor/Scripts
- Official/community Scripts repository is MIT.
- If Codex has internet, it may fetch/pin it as an optional automation reference.
- Imported scripts remain untrusted until reviewed.

## BestPracticeRules
- Use official rules/repository as a behavior/reference source subject to license verification at implementation time.
- PbiBench may author its own rules independently.

## Microsoft PBIR/TMDL/Fabric APIs
Use public Microsoft documentation/schemas/interfaces.

## SQLBI
Knowledge source only:
- metadata/URLs,
- our summaries/notes,
- user-provided local samples.
Do not mirror copyrighted article bodies.

## VizForge
Original spec/themes/UI.
Use official D3 and Microsoft Power BI visual tooling under their respective licenses.

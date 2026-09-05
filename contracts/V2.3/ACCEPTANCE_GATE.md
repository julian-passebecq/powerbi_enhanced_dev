# V2 Pass 3 acceptance gate

## Architecture
- Report Studio no longer references PbiBench.DaxStudio.
- new neutral ExternalTools library owns generic launcher/discovery/quoting.
- DaxStudio module depends on ExternalTools and contains DAX-Studio-specific logic only.
- module catalog matches actual project-reference closure.
- Report Studio remains TE2/Fabric-auth free.
- Fabric Toolbox remains TE2/Semantic-UI free.

## Unified product
- PbiBench.exe is normal entry point.
- Home/Start Center exists.
- compact module rail exists.
- Report/Fabric launch/focus child modules.
- consistent project context strip.
- child apps look like PbiBench family members.
- no fourth launcher executable.

## Design Exchange
- exports provider-neutral model context.
- sensitive fields remain excluded by default.
- dashboard-spec strict validation.
- theme JSON strict validation.
- invalid semantic bindings are explicit.
- no provider/network call.
- no arbitrary script/expression in dashboard spec.
- validated spec/theme can be opened in Report Studio design-preview route.

## UI/icons
- coherent light theme.
- pinned SVG subset or original vectors.
- provenance/license recorded.
- no Data Goblin assets/code copied.

## External tools
- existing DAX Studio handoff still works.
- no DAX Studio feature expansion.
- Bravo/Desktop/VS Code launch behavior preserved.

## Regression
- V11 portable regressions green.
- V2 tests green.
- ExternalTools tests green net10/net48.
- Design Exchange contract tests green net10/net48 where applicable.
- focused WPF shell/Report Studio tests green.
- process isolation green.
- one final impacted Release gate passes.

# Screenshot file audit

The screenshot showed 22 ZIP entries.

The current mounted workspace contains exact filename matches for 18.

## Missing exact files

- `Power-BI-Design-Files-main (1).zip`
- `Power-BI-Design-Files-main.zip`
- `Sales_report_using_PowerBi-main.zip`
- `powerbi-modeling-mcp-main.zip`

The two `Power-BI-Design-Files` entries appear to be two copies/versions of the same repository, so this is **four missing ZIP files but three likely unique repositories**.

`powerbi-modeling-mcp-main.zip` is not required as a bundled source dependency. PbiBench should consume the current Microsoft Power BI Modeling MCP as an external preview runtime/package and respect its current license/EULA.

The missing design/sample repositories are optional references, not blockers for core development.

# DAX Query and External Tool Hub

## Keep PbiBench DAX Query first-class

PbiBench remains the everyday DAX workbench.

Primary actions:
- Format DAX
- Run all
- Run selection
- Run current logical statement where parser proves it
- Cancel
- Open/save `.dax`
- History
- Results grids
- Problems/diagnostics
- Semantic tests
- Analyze in DAX Studio

Recommended layout:

```text
+------------------+---------------------------+------------------+
| Model Explorer   | DAX editor/query tabs     | Context / Help   |
+------------------+---------------------------+------------------+
| Results | Problems | History | Tests | Output                  |
+----------------------------------------------------------------+
```

Context panel:
- selected DAX function signature;
- short local description;
- parameter hints;
- model object definition/dependencies;
- `Open in DAX Guide` browser link.

Do not copy DAX Guide article content into PbiBench.

## External Tool Hub

Add compact buttons in DAX toolbar and Apps/Tools.

### 1. DAX Studio — deep performance
Button:
`Analyze in DAX Studio`

Behavior:
- reuse current existing PbiBench handoff;
- pass current server/database context;
- pass current query through the supported file/argument route;
- do not duplicate Server Timings/query-plan specialist depth in PbiBench.

### 2. Bravo — quick model helper
Button:
`Open in Bravo`

Current Bravo public CLI supports:
- `--server`
- `--database`
- optional parent process ID in packaged scenarios

Launch only when:
- Bravo executable is detected/configured;
- a compatible live semantic-model server/database context exists.

Example concept:
`Bravo.exe --server="<server>" --database="<database>"`

Do NOT pass the current DAX query: Bravo's current public startup parser does not expose a query argument.
Do NOT promise a direct launch into a specific Bravo page.

After launch, Bravo provides its own:
- Analyze Model
- Format DAX
- Manage Dates
- Export Data

This is a companion workflow, not embedded Bravo.

### 3. Power BI Desktop
Buttons:
- `Open Project in Power BI Desktop`
- `Open Current Report`

Prefer:
- `.pbip` if present;
- otherwise a known `definition.pbir` route that Desktop supports.

Do not invent a PBIX path.

### 4. Report Studio
Button:
`Open in Report Studio`

Enabled when:
- PBIP project/report path is known.

Handoff contains paths/IDs only, no credentials.

### 5. VS Code
Button:
`Open Project in VS Code`

Open PBIP/project folder if VS Code is detected.

## Tool status

Apps/Tools should show:
- Installed
- Missing
- Configured path
- detected version where practical
- current connection/project applicability

Tool bridges are separate update lanes.

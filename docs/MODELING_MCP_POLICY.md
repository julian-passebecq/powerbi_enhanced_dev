# Power BI Modeling MCP policy

The screenshot's local `powerbi-modeling-mcp-main.zip` is missing.

That is **not a blocker**.

PbiBench should not depend on a frozen copied source ZIP.

Use the current Microsoft-published MCP runtime/package externally.

## Why

The server is:
- Public Preview,
- updated independently,
- a first-party semantic-model automation surface,
- capable of bulk model operations,
- capable of connecting to Desktop, Fabric and PBIP/TMDL.

The current repository also carries preview license/EULA language that must be checked at deployment time.

## PbiBench integration

```text
PbiBench.Agent
  -> MCP client
     -> external Power BI Modeling MCP process
```

Default:
- start/read in read-only mode where supported,
- show tool version and license status,
- never silently install/update,
- mutation requires PbiBench plan + snapshot + approval.

PbiBench must remain functional without MCP through its direct TOM/TMDL semantic engine.

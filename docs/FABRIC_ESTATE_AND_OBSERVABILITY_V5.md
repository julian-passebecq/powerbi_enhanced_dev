# Fabric Estate / Observability

## Estate page
Build a read-mostly operational view before any tenant admin write capabilities.

Show:
- workspaces
- capacities
- items by type
- reports
- semantic models
- refreshables/status
- Git connection status when available
- ownership/roles
- last updated
- deployment stage metadata if discoverable
- warnings/previews.

## FUAM
Do not rebuild FUAM immediately.
Use it as:
- deployment/reference option
- monitoring data source later
- example of Fabric-native tenant telemetry architecture.

PbiBench can detect whether FUAM appears deployed and optionally link to its Lakehouse/report.

## FCA
Treat Fabric Cost Analysis as a separate FinOps accelerator.
PbiBench should link/integrate cost signals later rather than copy the whole solution.

## MicrosoftFabricMgmt
Use the Microsoft Fabric Toolbox PowerShell module as:
- API coverage reference
- fallback/advanced command launcher
- source of tested patterns for LRO/retry/error/output behavior.

C# REST adapters remain the main PbiBench control-plane implementation.

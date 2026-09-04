# Power BI Service Control Plane

Power BI service remains useful alongside Fabric APIs.

## API family
Base: `https://api.powerbi.com/v1.0/myorg/`

Initial functionality:
- workspaces/groups
- reports
- semantic models/datasets
- data sources
- refresh status/trigger
- imports if needed
- admin inventory when authorized.

## .NET SDK
Microsoft maintains `Microsoft.PowerBI.Api`.
Create an adapter behind `IPowerBiServiceClient` after current NuGet package version is pinned.
Do not expose SDK DTOs to Core domain models.

## Why both Power BI REST and Fabric REST
The services overlap but are not identical.
The transport router should select the API that currently supports the exact operation and identity mode.

Examples:
- Fabric item definition -> Fabric REST
- Power BI Admin dataset data sources -> Power BI Admin REST/.NET SDK
- precise semantic object mutation -> XMLA/TOM
- report/semantic item estate -> Fabric Admin or Power BI Admin depending scope/status.

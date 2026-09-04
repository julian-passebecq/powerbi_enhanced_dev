# Security, Authentication, Approval

## Auth architecture
Define `IAccessTokenProvider` in core-facing infrastructure boundary.
Production implementations later:
- delegated interactive MSAL.NET
- confidential client/service principal
- managed identity for hosted/CI mode.

Never persist access tokens.
Store account metadata only when needed.
Use OS secure credential facilities for secrets.

## Scopes
The app must request the minimum scopes required for the selected operation.
Do not request Tenant.ReadWrite.All for normal report development.

## Approval levels

0. Inspect local/read metadata
1. Query/benchmark
2. Propose/diff only
3. Reversible local mutation
4. Remote semantic model mutation
5. Workspace/item permission or deployment mutation
6. Capacity/tenant-admin mutation

Levels 4-6 always show:
- target tenant/workspace/item
- identity
- exact operation
- before snapshot
- rollback/restore strategy
- validation plan.

## Audit
Every remote call records:
- UTC timestamp
- adapter
- operation name
- target resource IDs (not secrets)
- HTTP status
- correlation/operation ID
- elapsed time
- approved plan ID for mutation
- outcome.

Never log Authorization headers or tokens.

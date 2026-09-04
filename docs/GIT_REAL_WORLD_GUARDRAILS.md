# Git / PBIP real-world guardrails

The user-provided community discussion highlights recurrent Power BI Git friction.

Treat these as UX requirements, not authoritative Microsoft specifications.

## Guardrails

### Local data cache is not Git content
Never encourage committing multi-GB model cache files.

### Thin report path
When a governed semantic model already exists, recommend a thin report rather than forcing every developer to hold the imported model locally.

### Development subset
Support optional development parameters/sample-data profiles for local model work.

### Workspace Git is optional
PbiBench's primary DevOps model should work with normal local Git + CI/API deployment.
Fabric workspace Git integration can be an additional deployment strategy.

### Validate before publish
Typical gate:
1. branch
2. local model/report changes
3. PbiBench validation
4. pull request
5. automated rules/tests
6. deploy to controlled workspace
7. refresh
8. smoke tests.

### Path doctor
PBIP can generate deep paths.
`doctor` should:
- warn about long root paths,
- check Windows long-path settings where possible,
- check `git core.longpaths`,
- recommend short local repo roots,
- warn about mixing OneDrive/SharePoint sync with a Git working tree.

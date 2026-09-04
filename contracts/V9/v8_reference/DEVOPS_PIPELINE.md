# PbiBench DevOps pipeline

The TabularEditor DevOps example demonstrates a useful minimal model CI pipeline:

1. load model source
2. run BPA
3. schema check
4. optionally rewrite development credentials/connection
5. deploy to Analysis Services / Power BI XMLA.

PbiBench should generalize this through its own CLI.

## Suggested commands

```text
pbibench validate model <path>
pbibench bpa <path>
pbibench schema-check <path> --profile Dev
pbibench dax-test <path>
pbibench diff <path>
pbibench deploy <path> --target Dev
```

## CI gate

Pull request:
- parse/load
- TMDL round trip
- BPA
- DAX tests
- source schema contract
- secrets scan
- semantic diff artifact

Main/release:
- same validations
- deployment plan
- explicit environment target
- deploy
- refresh
- smoke queries

## Security

Never put connection strings/passwords in source.

Use:
- GitHub Actions secrets / OIDC
- Azure DevOps secure variables / service connections
- Entra service principal / managed identity where supported.

The old TE2 DevOps example is a pattern, not a security/configuration template to copy literally.

# Semantic CLI in CI

`scripts/invoke-semantic-ci.ps1` runs the same command services used by the desktop workbench. It launches the CLI without a visible window or input prompt and retains each command's JSON, stderr, exit code and elapsed time in a fresh output directory.

Example after building Release:

```powershell
./scripts/invoke-semantic-ci.ps1 `
  -ModelPath ./MyProject.SemanticModel/definition `
  -BaselinePath ./baseline/definition `
  -OutputDirectory ./artifacts/semantic-ci
```

The steps inspect native metadata, validate TOM/TMDL round trips and DAX diagnostics, run versioned BPA rules, and export a property-level semantic diff when a baseline is supplied. Export or stage the baseline separately; the command does not check out another Git revision or change the working tree. `-FailOn Error` is the default. Warning/Information can enforce a stricter threshold; None still exports findings but does not fail on their severity.

Read-only engine assertions require an explicit test artifact and accessible target:

```powershell
./scripts/invoke-semantic-ci.ps1 `
  -ModelPath ./MyProject.SemanticModel/definition `
  -OutputDirectory ./artifacts/semantic-ci-connected `
  -TestsPath ./tests/revenue.pbitest `
  -Server $env:PBIBENCH_TEST_SERVER `
  -Database $env:PBIBENCH_TEST_DATABASE `
  -ConnectionEnvironmentVariable PBIBENCH_TEST_CONNECTION
```

The credential argument is an environment-variable **name**, not a secret. Set its value through the CI platform's secret store. The CLI supplies it transiently to the independent query connection. The CI summary states whether assertions were requested; inspect the test report's execution IDs and outcomes to determine which actually ran. Offline validation is never reported as successful engine validation. No deployment or refresh is performed by this script.

For separately reviewed deployment, the CLI supports `deploy --model SOURCE --server ENDPOINT --database NAME_OR_ID --review-out REVIEW.json --json --non-interactive`. Inspect the returned exact change review and target, then use `apply --review REVIEW.json --approve HASH --json --non-interactive` with the approved hash. Remote reviews expire and are claimed before execution; a failed or uncertain attempt requires a new review. Environment credentials are supplied again at apply time. Do not derive approval automatically from generated model text. Refresh uses the same preview/approval flow with its typed refresh options. A successful deployment does not imply that data processing or semantic tests succeeded.

`scripts/invoke-cli-smoke.ps1` checks the actual packaged/built executable using an isolated profile and private demo copies. It covers JSON and stderr, exit codes, profiles, native model reads, BPA thresholds, TMDL validation, semantic diff, exact Unicode/quoted property values, separate-process preview/apply, measure creation, gallery actions, stale/forged/replayed reviews and missing connected targets. It is part of the full build and package scripts. These fixtures do not submit remote writes or call an AI provider.

# Tests Codex must add

## Unit
- ToolRouter transport selection
- PBIP scanner warnings
- definition payload path-safety
- Fabric definition parsing
- 429 Retry-After behavior with fake HttpMessageHandler
- 202 LRO behavior with fake handler
- approval-level enforcement
- DAX Studio command construction

## Golden
- local/cloud TMDL snapshot comparison
- object-aware semantic diff
- PBIR diff grouping

## Integration (opt-in)
Environment variables only, never committed:
- PBIBENCH_TEST_TENANT
- PBIBENCH_TEST_WORKSPACE
- PBIBENCH_TEST_SEMANTIC_MODEL

Integration tests must be read-only by default.
Write tests require `PBIBENCH_ALLOW_REMOTE_WRITE_TESTS=true` plus a disposable target check.

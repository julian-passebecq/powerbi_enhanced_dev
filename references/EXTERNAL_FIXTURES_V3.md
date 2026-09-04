# External fixtures

Do not duplicate the very large `powerbi-desktop-samples-main (2).zip` in this handoff.

It is MIT licensed and should be configured as an external test corpus.

Suggested setting:

```text
PBIBENCH_POWERBI_SAMPLES_ROOT=C:\Fixtures\powerbi-desktop-samples-main
```

Useful groups:
- 2026 Power BI Samples Revamp
- DAX Adventure Works
- Performance Analyzer
- monthly feature samples.

PbiBench tests should skip the large corpus when the variable is not configured.

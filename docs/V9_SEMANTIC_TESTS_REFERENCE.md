# Semantic tests and background work

V9.4 adds original PbiBench assertion services and project artifacts. The GUI is in QA → Semantic tests. It uses the existing `IDaxQueryService` and its private per-run TOM connection; no query or cancellation is sent through the hosted editor connection.

## Assertions

- **Scalar** requires exactly one row and one column and checks the configured typed value/comparison.
- **RowCount** checks the exact retained row count against the configured expected count and comparison.
- **Table** checks every value in the selected 1-based GUI column. An empty result fails a table assertion; use a row-count assertion when emptiness is intended.
- **Snapshot** compares ordered column captions/types and every ordered value against an explicitly captured expected result. A virtualized review grid shows the returned values before the author accepts or cancels baseline replacement. Accepting a baseline does not prove those values are correct.
- **CompareQueries** runs query A and query B separately on the same selected model and compares their ordered schemas and results. It measures output equivalence, not performance or transactionally consistent source data. Concurrent model/data changes can affect comparison; the VertiPaq workspace provides the separate bounded benchmark workflow.

Snapshot and A/B modes require an explicit deterministic-order confirmation. Authors must use a suitable `ORDER BY`, including unique tie breakers where necessary, or otherwise establish deterministic output. PbiBench cannot infer uniqueness from arbitrary query text. Snapshot baselines are bound to the exact DAX hash; changing the query requires a new baseline.

BLANK, number, text, Boolean and date/time values retain distinct types. Numbers use invariant decimal coefficients, preserving Int64 and Decimal precision and round-trip engine floating-point values, including subdecimal values. Values outside finite floating-point range or below its representable nonzero range are rejected. Numeric equality uses `abs(a-b) <= absoluteTolerance + relativeTolerance * max(abs(a),abs(b))`; zero tolerances require exact numeric equality. Strict numeric ordering excludes values equal within tolerance. Text compares ordinally. Dates with offsets compare UTC instants; dates without offsets compare calendar ticks. Mixing zoned and unzoned date/time values is an error, so the machine timezone cannot silently change test results.

Every assertion requires exactly one complete result set with a matching query, server, database and document revision. Missing/extra results, malformed shapes, any query warning, truncation and provider failures cannot pass. Runs retain a real execution id from the query service. Cancellation produces no passing result. Model changes and draft changes invalidate displayed evidence; completed reports describe only their recorded run.

## Files and limits

Save tests as `*.pbibench-tests.json` alongside a project and commit them through the normal workspace/Git workflow. Artifacts use version 1, typed values and explicit stable test ids. Import rejects unknown fields, unknown versions and missing identity/query/value fields. Files contain authored DAX and optional expected data; they do not contain endpoints or connection strings. Reports contain assertion outcomes, timestamps, query hashes and execution ids; provider exception text and transport credentials are excluded.

Bounds: 200 tests per artifact, 16 MB per file, 1 million characters per query, 100,000 rows per test, 250,000 retained cells, 1,000 columns and configurable 1–3,600 second timeout. Bounds limit retained data and time; they do not estimate engine work. Use a row-count query for a large-table count instead of fetching the entire table. Atomic sibling-file replacement preserves an existing artifact when a write fails or is canceled before commit.

## Shared queue

`BackgroundTaskQueue` bounds active plus queued work (default 32) and concurrent workers (default 2). Callers capture immutable/detached inputs on the UI thread, then enqueue work. Status, progress, cancellation requests and up to 100 completed entries appear in Output → Background tasks. Generic results are returned to the initiating view through the task handle; they are not serialized by the queue. Errors retain only the exception type in shared status, avoiding provider-message credential leaks. Cancellation is cooperative; an operation that actually completes is marked completed even if cancellation arrived too late. Disposing a view or queue never waits on engine I/O on the UI thread.

Automated service fixtures verify comparison boundaries, result provenance, cancellation, stale views, atomic artifacts and queue limits. These fixtures are not evidence of a successful populated-model engine run; connected-engine validation remains a separate integration check.

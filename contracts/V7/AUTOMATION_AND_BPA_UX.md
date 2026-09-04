# Automation + BPA UX

## Automation Gallery

Each action card shows:
- name
- short purpose
- scope
- supported object types
- risk
- last run
- Scan button

After scan:
- findings count
- exact object list
- preview

After preview:
- before / after
- validation plan
- Apply

After apply:
- validation result
- Undo
- Accept

## BPA findings

Represent BPA as engineering findings rather than a simple list.

Example:

```text
WARNING
Customer[CustomerKey]

Numeric key column is summarized by Sum.

Current:
  SummarizeBy = Sum

Recommended:
  SummarizeBy = None

Risk:
  SAFE

[Go to object] [Preview fix]
```

For performance-oriented advice:

```text
REVIEW / BENCHMARK REQUIRED
FactSales[OrderId]

High-cardinality hidden column may not need MDX attribute availability.

Do not auto-apply.

[Explain] [Benchmark plan]
```

The product should teach the user why an action is proposed.

# SQLBI Knowledge Radar

## Purpose

Turn current DAX/Power BI research into actionable engineering hints without copying SQLBI content.

SQLBI states that its site content is copyrighted / all rights reserved.

Therefore PbiBench stores:
- title
- URL
- date
- topic metadata
- user notes
- PbiBench-generated short summary
- local sample file path
- related model objects
- implementation status.

No full-article mirroring.

## Seed topics for September 2026

1. Testing DAX measures by using AI
   - convert business logic into repeatable test cases
   - especially filter-context tests.

2. Creating DAX functions with AI to remove duplicated code
   - detect duplication
   - UDF refactor suggestion
   - choose model-dependent vs model-independent.

3. Visual calculation performance
   - compare measure vs visual calculation
   - monitor virtual-table densification.

4. Dynamic formatting by hierarchy level
   - model measure approach with ISINSCOPE
   - visual calculation approach with ISATLEVEL.

5. UDF vs calculation groups
   - implementation reuse vs user-facing transformation.

6. Model-dependent vs model-independent UDFs
   - portability and schema dependency.

7. REMOVEFILTERS in UDFs
   - modifier-returning function patterns.

8. Direct Lake vs Import
   - scenario planning / performance.

9. ALL / ALLSELECTED / ALLEXCEPT / REMOVEFILTERS
   - filter-context linting and teaching hints.

10. Matrix totals / visual behavior
   - total-context test cases.

## UI

```text
Knowledge Radar
  [DAX] [Model] [Visual Calc] [Performance] [Direct Lake] [AI]

  Latest / Relevant to current model

  Article card
    title
    date
    topic
    why relevant
    Open source
    Add note
    Attach local sample
    Generate test idea
    Generate BPA/action idea
```

## AI behavior

When user selects "Review against current model":
1. load current model metadata,
2. load only allowed source metadata/short public snippets or user-provided article text,
3. generate an original checklist,
4. link every idea to its source URL,
5. never claim SQLBI endorses PbiBench's recommendation.

## Optional feed

Use SQLBI's publicly exposed article pages/RSS metadata for title/date/link discovery.

Do not cache article bodies unless the user explicitly saved them locally.

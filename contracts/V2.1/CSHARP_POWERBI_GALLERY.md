# Power BI C# Automation Gallery

## Goal

Expose the most useful Power BI semantic-model automation as a curated gallery instead of making users remember script names.

Do not dump an entire community repository into the UI.

Each card:
- title;
- category;
- one-line purpose;
- required selection;
- parameters;
- `SAFE RECIPE`, `SAFE SCRIPT` or `TRUSTED C#`;
- model/storage compatibility;
- risk hints;
- source/provenance/license;
- `Preview`, `Insert C#`, `Open source`.

Trusted code never auto-runs.

## Tier 1 — PbiBench Verified Essentials

### Measures
1. Create SUM measures from selected numeric columns.
2. Create COUNTROWS measures from selected tables.
3. Create explicit measures from aggregatable columns.
4. Create a measure table.
5. Bulk set format strings.
6. Move selected measures to display folders.

### Model hygiene
7. Set `SummarizeBy=None` for selected columns.
8. Hide technical / many-side relationship key columns.
9. Clean object names with exact preview.
10. Add/update descriptions from a bounded template.

### Modeling
11. Dynamic measure selector.
12. Basic time-intelligence calculation-group template.
13. Inactive-relationship / USERELATIONSHIP helper where appropriate.

### Quality
14. Invalid/broken object-reference scan.
15. Relationship coverage helper.
16. Date gaps / string quality / outlier helper.

## Native-first rule

Some community C# scripts should become native PbiBench commands rather than remain Trusted scripts.

Examples:
- model profiling -> use PbiBench Explore/Profile;
- DAX export -> use PbiBench export;
- relationship coverage -> use existing PbiBench quality/profile service;
- bulk metadata actions -> Safe Recipe where supported.

Gallery may still show:
`Implemented natively from this common automation pattern`

and provide attribution/provenance.

## Verified upstream source families

### TabularEditor/Scripts — MIT
Useful starting scripts include:
- Autogenerate SUM Measures
- Create countrows measures
- Format All Measures
- Hide columns on the many side of a relationship
- Move All Columns to a DisplayFolder
- Clean object names
- Create Time Intelligence Measures Using Calculation Groups
- CreateExplicitMeasures
- Dynamic measure selector
- Data Profiling helpers: column profiles/distributions, date gaps, outliers, relationship coverage, string quality

### Tabular Editor official C# Script Library
Use as a compatibility/curation reference. Official docs state these scripts are verified/validated by the Tabular Editor team, while still executed at user responsibility.

## Parameters

Do not hard-code:
- display folder names;
- number formats;
- prefixes/suffixes;
- measure naming;
- description text.

Use typed bounded parameters and show exact generated source before insertion.

## Suggested Gallery UX

Filters:
`Essentials | Measures | Modeling | Hygiene | Quality | Community`

Badges:
`SAFE` `REVIEW` `TRUSTED`

Sort:
- Recommended
- Most used
- Recently used
- Favorites

A macro saved by the user is not automatically a Verified Gallery item.

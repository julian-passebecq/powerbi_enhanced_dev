# Senior Power BI Playbook

PbiBench should contain a context-sensitive Senior Playbook.

This is not a static blog reader. Each tip has:
- trigger/detection
- explanation
- confidence/source freshness
- risk level
- whether it is auto-fixable
- validation/benchmark
- rollback.

## Tip classes

### AUTO-CHECK
Safe to detect automatically.

### PROPOSE-FIX
Can create a dry-run change plan.

### BENCHMARK-ONLY
Never apply automatically; test before/after.

### REMINDER
Architecture/workflow advice shown when context matches.

---

## 1. IsAvailableInMDX on non-user-facing columns

**Class:** BENCHMARK-ONLY / PROPOSE-FIX

Detect:
- hidden keys
- technical/fact columns
- columns not intended for Excel/MDX consumption.

Proposal:
- consider `IsAvailableInMDX = false` selectively.

Why:
- can reduce attribute-hierarchy overhead/model size and processing time.

Do not blindly disable:
- Analyze in Excel / MDX consumers may need columns,
- some DISTINCTCOUNT/MIN/MAX scenarios can benefit from attribute hierarchies.

Validation:
- VPAX before/after
- refresh duration
- representative DAX/MDX queries
- Excel compatibility checklist.

---

## 2. Discourage implicit measures

**Class:** PROPOSE-FIX

Recommend when:
- calculation groups exist or are planned,
- organization wants governed explicit measures.

Benefits:
- calculation groups apply to explicit measures,
- can reduce unnecessary Filter pane count queries.

Validation:
- existing report visual audit before enabling.

---

## 3. ValueFilterBehavior review

**Class:** REMINDER / TEST

For compatibility level that supports it:
- inspect model `ValueFilterBehavior`.
- test `Independent` when SUMMARIZECOLUMNS filter behavior is surprising.

Do not change automatically.

Generate a DAX regression query that demonstrates relevant filter semantics before proposing a change.

---

## 4. DirectQuery parallelism

**Class:** BENCHMARK-ONLY

Check:
- MaxParallelismPerQuery
- Maximum connections per data source
- number of page visuals/interactions
- independent vs dependent Storage Engine requests.

Important:
- more parallelism can reduce latency,
- dependencies in DAX can serialize requests,
- more concurrency can make the data source slower.

PbiBench action:
- generate benchmark matrix
- open DAX Studio Server Timings
- record source load and elapsed time.

Never auto-maximize concurrency.

---

## 5. Strict vs eager branch evaluation experiment

**Class:** BENCHMARK-ONLY

When DirectQuery Server Timings show dependencies caused by branching logic:
- suggest testing an equivalent `IF.EAGER` variant when semantics allow.

This is an experiment, not a universal optimization.

---

## 6. Visual calculation vs measure

**Class:** BENCHMARK-ONLY

If a calculation is visual-local:
- generate measure and visual-calculation candidates,
- estimate visual-shape/densification risk,
- benchmark both.

Do not assume visual calculations are faster.

---

## 7. Repeated DAX -> UDF candidate

**Class:** PROPOSE-FIX

At compatibility level 1702+:
- scan repeated expressions,
- propose model-dependent or model-independent UDF,
- generate DAX tests before refactor.

---

## 8. User-aware calculated columns

**Class:** REMINDER

For localization/security-style query-time scenarios:
- consider calculated column Expression Context = User Context.

Warn:
- query-time evaluation can affect performance,
- use only when user-dependent behavior is actually required.

---

## 9. PBIP external-edit guard

**Class:** AUTO-CHECK

Before external model/report edits:
- detect `unappliedChanges.json`
- refuse expression edits until pending changes are applied.

Also warn:
- some PBIP files remain unsupported for external editing during preview,
- `cache.abf` is not reloaded by Desktop Bridge.

---

## 10. PBIP path doctor

**Class:** AUTO-CHECK

Check:
- project root length
- likely 260-character path risk
- Git long-path configuration
- OneDrive/SharePoint synchronized working tree.

Recommend a short local Git root.

---

## 11. Do not edit automatic date tables externally

**Class:** AUTO-CHECK

Warn and block automated edits to Power BI automatic date tables.

---

## 12. Composite / Direct Lake synced object protection

**Class:** AUTO-CHECK

When modifying properties sourced from other models/sources:
- preserve required changed-property/removal annotations.

Treat schema synchronization as a first-class risk.

---

## 13. DAXQueries are code

**Class:** REMINDER

Save regression/performance queries as `.dax` files in the PBIP project's DAXQueries folder.

Commit them with the model so tests live beside code.

---

## 14. Thin report suggestion

**Class:** ARCHITECTURE REMINDER

When a governed semantic model already exists and developers do not need local model edits:
- recommend thin reports/live connection rather than cloning full imported data locally.

---

## 15. Development data profile

**Class:** ARCHITECTURE REMINDER

For very large import models:
- offer a dev/sample parameter profile,
- never confuse development row limiting with production deployment settings.

---

## 16. Page visual/interaction pressure

**Class:** AUTO-CHECK / BENCHMARK

DirectQuery page:
- count visuals,
- count cross-filter/highlight interactions,
- flag expensive fan-out,
- suggest performance capture before adding more visuals.

---

## Tip UI

```text
Senior Playbook finding

DirectQuery concurrency may be source-limited
Risk: Medium | Action: Benchmark only

Why shown:
  13 visuals on page
  Maximum connections = 10
  query timeline shows independent requests

Try:
  [Open DAX Studio]
  [Generate benchmark]
  [Read source]
  [Dismiss for this model]
```

# Architecture decision V4

## Candidate A - TE2++ monolith

Fork TE2 and add everything directly to its WinForms application.

### Pros
- Fastest path to semantic-model editing.
- Existing TOM wrapper, BPA, scripting, selection and undo concepts.
- One executable.

### Cons
- Legacy WinForms architecture becomes permanent technical debt.
- Report/PBIR/VizForge/Git/agent UX does not naturally fit.
- Hard to separate live model state from disk PBIP state cleanly.
- Temptation to transplant DAX Studio or mimic TE3 too closely.

**Use only as an internal prototype, not the long-term architecture.**

---

## Candidate B - PbiBench shell + TE2-derived semantic engine + DAX Studio bridge

### Pros
- Best licensing boundary.
- Modern UI can be designed around PBIP/Git/report/AI workflows.
- TE2 logic remains reusable.
- DAX Studio remains intact for Server Timings, query plans and advanced diagnostics.
- Power BI Desktop remains render/publish authority.
- Allows original implementations of selected TE3-like workflows.

### Cons
- More engineering upfront than modifying TE2 forms.
- Requires adapters between live model, disk model and external tools.

**RECOMMENDED.**

---

## Candidate C - Orchestrator only

Keep TE2 and DAX Studio completely untouched and build only a guide/launcher/knowledge application.

### Pros
- Lowest risk.
- Quickest useful V1.
- Minimal licensing exposure.

### Cons
- Less differentiated.
- Bulk actions and model changes remain fragmented.
- User still experiences multiple disconnected applications.

**Good V1 fallback, but not the final product.**

---

## Candidate D - VS Code extension first

Build Power BI engineering around VS Code and PBIP files.

### Pros
- Excellent Git/source workflow.
- Natural TMDL/PBIR/DAX text editing.
- Good fit for Codex.

### Cons
- Weak live-model exploration.
- Weak report/render/data-preview experience.
- Not a replacement for semantic IDE behavior.

**Make this an optional later companion, not the primary application.**

---

## Candidate E - Merge TE2 + DAX Studio + everything

### Verdict: Do not do this.

Reasons:
- incompatible architectural assumptions,
- different licenses,
- duplicated connection/model concepts,
- huge maintenance surface,
- unclear UX boundaries,
- effort better spent on unique PbiBench capabilities.

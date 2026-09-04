# PbiBench V8 — TE2 Boost Pack

This is a **delta pack** for the current working PbiBench repository.

Do not replace the repository.
Do not interrupt the current Pass 1.5 UX run if it is still in progress.

Apply this pack **after the V7 Pass 1.5 acceptance gate**.

## Why this exists

New material was reviewed from:
- current TE2 open pull requests/issues,
- current Tabular Editor documentation,
- public Tabular Editor 3 capability documentation,
- TabularEditor Scripts,
- TabularEditor DevOps examples,
- Best Practice Rules,
- current 2026 UDF/TMDL/refresh/data-preview behavior.

The goal is to make PbiBench's TE2++ semantic editor materially stronger without attempting to clone TE3.

## Important legal/source clarification

The supplied `TabularEditor3-master` archive is **not the Tabular Editor 3 product source code**.
It is the public community/support repository plus third-party notices/media.

A paid TE3 license does not by itself grant rights to create derivative works of TE3 proprietary product code.

Therefore:
- use TE2 MIT source directly,
- use MIT TabularEditor Scripts directly with attribution,
- use public TE3 docs/observable behavior as a requirements benchmark,
- independently implement equivalent workflows,
- reuse only third-party open-source components under their own licenses,
- do not decompile/copy proprietary TE3 implementation/assets.

## Read order

1. `PASTE_THIS_TO_CODEX.txt`
2. `MASTER_PROMPT_V8.md`
3. `UPSTREAM_TE2_PATCHES.md`
4. `ISSUE_DRIVEN_HARDENING.md`
5. `TE3_CAPABILITY_BOOST.md`
6. `SCRIPTS_TO_TYPED_ACTIONS.md`
7. `DATA_PROFILING_LAB.md`
8. `DEVOPS_PIPELINE.md`
9. `BPA_STRATEGY.md`
10. `ACCEPTANCE_GATE_V8.md`

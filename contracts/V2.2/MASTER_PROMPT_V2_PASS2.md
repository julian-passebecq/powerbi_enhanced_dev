# Master Prompt — V2 Pass 2

Baseline: `1e02628f7b35af0e5b92c0452f86d3b102562cc2`

Objective:
deepen the successful Gen-2 product and correct the independent audit gaps.

Workstreams:
A. Hosted V2 CI and stale workflow naming.
B. TMDL/semantic catalog correctness + cross-layer report impact.
C. Report Studio UX/action depth.
D. C# Automation Gallery provenance + selected advanced templates.
E. Fabric Toolbox read-only report snapshot using public getDefinition.
F. Catalog/documentation cleanup.

Architecture remains:
- Semantic IDE net48 + TE2 foundation;
- Report Studio separate modern process and TE2-free;
- Fabric Toolbox separate modern process and auth owner;
- external DAX Studio/Bravo/Desktop/VS Code;
- local PBIR writes reviewed;
- no remote report writes this pass.

Suggested product version: `2.2.0`.

Commit:
`v2-pass2 - Report engineering depth, impact review and V2 CI`

Testing:
targeted while editing, one final impacted Release gate before push, hosted CI after push, external reviewer audits results.

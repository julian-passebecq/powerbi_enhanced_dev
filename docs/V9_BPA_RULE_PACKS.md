# Original PbiBench BPA packs

PbiBench ships eight independently authored packs, version 1.0.0: Naming, Formatting, Modeling, Performance, Security, DAX, Direct Lake and PBIP/Git. The Rule packs tab lists all 16 rules, applicability, severity, provenance and fix risk. The existing native TE2 BPA remains available for user/community rule files. PbiBench does not ship the unlicensed rule snapshot from the contract reference material or execute imported `FixExpression` text.

The five established companion rules retain their reviewed TE2 metadata actions. Added policies inspect names, general number formatting, calculated-column candidates, security propagation, roles without row filters, division operators, Direct Lake metadata and observed Git state. These are authoring prompts, not proof that a model is incorrect or slow. Performance findings require measured evidence; they never offer automatic performance rewrites. Security rules explicitly require checking effective permissions and intended role behavior.

Fix classes are SAFE, REVIEW, BENCHMARK and MANUAL. Every available metadata fix still has an exact preview, stale-model guard and Undo. Settings can enable/disable individual rules, override severity and suppress a finding for its model source. Settings are bounded, versioned data written atomically; they cannot introduce executable predicates or fixes. Suppression identity includes a hash of the model/source identity, so matching object names in different sources are kept separate. A model edit invalidates stale displayed findings until the next scan.

The optimization cockpit receives current BPA evidence and semantic test outcomes through plain signals. It does not infer successful benchmarks from rule matches or accept passing tests for a different connection. VertiPaq metrics and queries have their own capture timestamps and limitations.

Public behavior references informing these original policies:

- [Microsoft star schema guidance](https://learn.microsoft.com/en-us/power-bi/guidance/star-schema).
- [Bidirectional relationship guidance](https://learn.microsoft.com/en-us/power-bi/guidance/relationships-bidirectional-filtering).
- [Import model data reduction](https://learn.microsoft.com/en-us/power-bi/guidance/import-modeling-data-reduction).
- [Row-level security](https://learn.microsoft.com/en-us/fabric/security/service-admin-row-level-security).
- [DIVIDE versus the division operator](https://learn.microsoft.com/en-us/dax/best-practices/dax-divide-function-operator).
- [Direct Lake overview](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview).
- [Power BI projects](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview).

Tests verify pack identity, read-only scans, explicit benchmark risk, source-aware suppression and severity overrides, workspace findings only with supplied observations, token-based division detection that excludes strings/comments, and canceled atomic profile writes preserving the old file. Full milestone evidence is recorded in `V9_IMPLEMENTATION_STATUS.md` when the gate passes.

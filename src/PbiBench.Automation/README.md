# PbiBench.Automation

`AutomationService` operates on the actual `TabularModelHandler` hosted inside PbiBench. Seven typed actions expose metadata, supported selection, risk, and exact before/after previews. Formatting an empty selection targets all measures only when `AllMeasuresWhenSelectionEmpty` is enabled. SUM creation requires numeric columns only and resolves measure/column name collisions before previewing.

The UI displays `ChangePreview.Changes` before calling `Apply`. Preview objects are immutable, bound to their service/session, and single-use. A full model fingerprint rejects stale previews. Apply uses one TE2 undo batch, checks postconditions, and rolls the entire batch back on failure. No action saves, deploys, refreshes, or writes to a remote server. Undo uses the existing TE2 stack.

`BpaService` adds original explanatory findings with severity, reason, source, before/after, live object navigation, and optional typed fix previews. Native TE2 BPA is preserved separately. Bidirectional filtering receives an advisory finding with no automated fix.

The measure table defaults to `_Measures`: TOM reserves the exact name `Measures`. The Last Refresh scaffold creates an import M partition capturing UTC at refresh time, a hidden timestamp column, and a measure. It adds metadata only; users must explicitly save/deploy and refresh to populate its value.

`tests/PbiBench.Semantic.Tests` characterizes TE2 dependency/rename/relationship/undo behavior and exercises action previews, all seven apply/undo round trips, collision handling, stale/replayed plans, unsupported selections, rollback after a setter failure, BPA, graph data, and formatter preservation.

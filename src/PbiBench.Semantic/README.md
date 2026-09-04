# PbiBench.Semantic

The Pass 1 semantic boundary targets .NET Framework 4.8 to share the pinned TE2 2.28.0 session with the hosted editor. It does not replace TE2 editing, dependencies, undo, or model loading.

`SemanticModelService(TabularModelHandler)` projects inventory and a relationship graph, preserving live table objects for click-to-select. Graph fact/dimension roles are inferred from relationship cardinality, not asserted as model semantics. Its whole-model SHA-256 fingerprint invalidates bulk previews after any metadata change.

`LocalDaxFormatter.Format` performs conservative layout formatting entirely offline. It preserves literal/identifier/comment tokens and verifies the result against the pinned TE2 lexer. It does not parse or execute DAX and does not contact a formatting service.

The upstream TOMWrapper must be built first. It is referenced as a compiled assembly to avoid importing legacy build settings into the SDK projects. Build and test through the repository scripts.

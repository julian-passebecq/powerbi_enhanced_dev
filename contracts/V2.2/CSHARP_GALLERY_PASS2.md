# C# Automation Gallery Pass 2

## Provenance model

Each card should distinguish:
- ImplementationOrigin: PbiBenchOriginal | PbiBenchNative | AdaptedMIT | ExternalReference
- ReferenceUrl optional
- ReferencePin optional
- License
- Verification: Verified | Preview | Reference
- ExecutionMode: NativeReadOnly | SafeRecipe | SafeScript | TrustedDraft

Examples:
- SUM measures: implementation PbiBench Safe Recipe; optional reference TabularEditor/Scripts MIT.
- BPA: implementation PbiBench native; no fake TabularEditor/Scripts implementation source.

## Expand to roughly 18–20 useful entries

Add only high-value items:
- annotation helper;
- translation/description helper;
- dynamic format-string helper where current typed actions support it;
- inactive relationship usage helper;
- dynamic measure selector template;
- time-intelligence calculation-group template;
- advanced calc-group scaffolding.

Advanced items that cannot be Safe Recipes are `TrustedDraft`:
- generate/insert text only;
- exact source preview;
- compatibility warnings;
- never auto-run.

Do not add Trusted profiling scripts when native Explore/Profile/BPA already does the job better.

## UX
Keep/add:
- search;
- category filter;
- favorites;
- recent;
- risk/mode badge;
- selection compatibility reason.

User macros never become Verified Gallery items automatically.

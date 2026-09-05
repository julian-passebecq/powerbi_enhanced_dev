# Design Exchange

A small provider-neutral bridge between PbiBench and external design tools / AI.

No embedded AI.

## Location

`Project > Design Exchange`

Also one Home card.

## Export — model context

`pbibench-model-context.json`

Reuse existing semantic/AI context DTOs where practical rather than inventing another model scanner.

Contract v1 includes:
- contractVersion
- model fingerprint
- model name
- compatibility level/storage metadata where non-sensitive
- tables
- columns
- data types
- measures
- DAX
- format strings
- descriptions
- relationships
- calculation groups / UDF metadata where available

Default EXCLUDES:
- credentials
- access tokens
- connection strings
- partition/source expressions
- gateway info
- raw rows
- local absolute paths

Optional explicitly enabled:
- bounded profile statistics
- bounded sample rows via the existing reviewed AI Context Export sampling system

## Import — dashboard design

`dashboard-spec.json`

Validate without mutating PBIR.

Required high-level fields:
- contractVersion
- modelFingerprint or explicit unbound mode
- report title/audience
- pages
- visual IDs
- visual kinds
- semantic bindings
- position/layout intent

Validation:
- IDs unique
- page/visual counts bounded
- coordinates finite/bounded
- supported visual type or explicit unsupported marker
- semantic table/column/measure exists
- binding kind matches semantic kind where known
- referenced model fingerprint mismatch is explicit
- no executable expressions/scripts
- unknown fields fail closed for mutation but can be shown in read-only diagnostics if contract policy allows

## Import — theme

`theme.json`

Validate against the pinned Power BI report-theme schema bundle.

Show:
- schema valid
- schema version
- theme name if present
- data colors
- recognized visualStyle families
- warnings for unsupported/new properties

Do not claim pixel-perfect preview.

## Result

Once model context + spec/theme are valid:

`Open in Report Studio`

Pass paths through a versioned handoff.

Pass 3 does NOT need to generate arbitrary full PBIR from dashboard-spec yet.

It should:
- load the design in Report Studio as a Design Preview / proposed layout overlay;
- show binding validity;
- allow future typed report-action generation.

PBIR generation/apply can be Pass 4 after the contract is proven.

## External AI prompt helper

`Copy Design Prompt`

Generate a short provider-neutral prompt saying:
- use model context as authoritative;
- do not invent semantic objects;
- return dashboard-spec.json;
- optionally return theme.json;
- no prose inside JSON.

No network call.

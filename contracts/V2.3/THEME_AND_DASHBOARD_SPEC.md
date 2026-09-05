# Theme JSON and dashboard-spec contract

## Why two files

`theme.json`
= real Power BI theme behavior.

`dashboard-spec.json`
= design intent: which pages, KPIs, charts, fields and rough layout.

Do not mix them.

## Minimal dashboard spec v1 example

```json
{
  "contractVersion": 1,
  "modelFingerprint": "sha256:...",
  "report": {
    "title": "Commercial Performance",
    "audience": "Executive"
  },
  "pages": [
    {
      "id": "executive",
      "title": "Executive Summary",
      "archetype": "Executive",
      "canvas": { "width": 1280, "height": 720 },
      "visuals": [
        {
          "id": "revenue-kpi",
          "kind": "card",
          "purpose": "Current revenue",
          "region": "top",
          "bindings": {
            "value": {
              "kind": "Measure",
              "table": "Measures",
              "name": "Revenue"
            }
          }
        }
      ]
    }
  ]
}
```

## Supported design kinds v1

Keep intentionally bounded:
- card
- kpi
- line
- area
- clusteredColumn
- stackedColumn
- bar
- donut
- table
- matrix
- slicer
- scatter
- combo
- waterfall
- text

Map to Report Studio/PBIR capabilities separately.

Unknown kinds:
- remain visible as unsupported design intent;
- do not synthesize a random PBIR visual.

## Theme Forge

Theme Forge remains external.

PbiBench only needs:
- import its exported theme JSON
- import its future dashboard-spec JSON
- export model context for it
- optional user-configured browser URL

No API or release coupling.

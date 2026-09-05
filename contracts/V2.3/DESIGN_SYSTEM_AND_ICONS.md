# PbiBench design system

## Direction

Use Data-Goblin-like clarity of capability groups, not their literal design/assets.

PbiBench should look like a restrained Microsoft-adjacent engineering IDE.

## Light-first tokens

- application background: light neutral
- panels/cards: white or near-white
- border: subtle gray
- text: near-black
- secondary: neutral gray
- one primary PbiBench accent
- state colors only for success/warning/error/info

Avoid:
- giant dark navy blocks
- heavy gradients
- rounded SaaS-card overload
- different saturated colors for every module

## Typography / spacing

Segoe UI/system font.

Spacing:
4 / 8 / 12 / 16 / 24 / 32

Corner radius:
4–8 px

Default button:
32–36 px height

## Icons

Preferred:
Microsoft Fluent UI System Icons, pinned SVG subset, MIT.

Suggested mapping:
- Home
- Model / database
- DAX / code
- Automate / flash
- Report / chart
- Project / folder-code
- Fabric / cloud
- Tools / plug
- Theme / color
- Git / branch
- Quality / shield-check
- External / open

Do not use icon fonts.
Do not share font files.
Do not copy Data Goblin SVGs.

Vendor only the exact SVGs needed and record:
- upstream
- tag/commit
- MIT
- local file list.

## Child modules

Report Studio and Fabric Toolbox should share:
- header hierarchy
- icon family
- background/surface/border tokens
- buttons
- status chips
- spacing
- `PbiBench / <Module>` breadcrumb

Do not embed the child windows into net48 just for visual consistency.

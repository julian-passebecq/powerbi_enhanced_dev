# Shared project context

The user should feel that all modules are working on one Power BI project.

## ProjectContext DTO v1

Metadata only:
- pbipRoot relative/absolute local path as appropriate for local process handoff
- semantic model path
- report path
- model fingerprint
- report fingerprint when known
- Fabric workspace/item IDs when explicitly selected
- Git branch/status summary
- source: Disk / Loaded / Live

Never include:
- token
- credential
- connection secret
- gateway key

## Process handoff

PbiBench -> Report Studio:
- project/report paths
- optional page/visual
- optional dashboard-spec/theme paths

PbiBench -> Fabric Toolbox:
- workspace/item IDs
- project path only if needed for local handoff

No WPF object references.
No TOM objects.
No singleton sharing.

## Components manifest

Add/update:
`components.json`

Contains:
- semantic IDE version
- report studio version/path
- Fabric Toolbox version/path
- ExternalTools contract version
- PBIR contract version

Apps/Tools and About read it.

This supports one product package with independently versioned components.

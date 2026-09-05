# Feature provenance and ownership

Baseline: `53813388fbf2a3d7075572fd0a33be207faeccdf`. Product: V11.1. Current code is authoritative; the original delta pack is retained under `contracts/V11.1`.

PbiBench owns its integration and new features. TE2 2.28.0 is the MIT semantic foundation, with third-party licenses preserved. Public TE3 capabilities are not an implementation source. No TE3 code/assets, Roslyn package or embedded AI requirement was introduced.

`provenance.json` is embedded into Core and displayed by Apps / Tools → Provenance. It records every major feature's adapter, protecting tests, upstream, pin, license classification and update lane. Original repository code is marked `unknown-needs-review` where no repository-wide license grant has been verified. Existing dependency inventories remain authoritative for package notices.

| Feature | Owner | Source | Pin | Update lane |
|---|---|---|---|---|
| Model tree / properties / undo | PbiBench.ModelEditor | te2-mit | 2.28.0 / 75f10e331b8de0dda5c213180b9b8867b4a38191 | te2 |
| Trusted C# compilation / execution | PbiBench.ModelEditor | te2-backed | 2.28.0 | te2 |
| Native DAX / C# text controls | PbiBench.App / PbiBench.ModelEditor | third-party-package | 2.16.24 | editor-control |
| DAX language intelligence / grammar catalog | PbiBench.Dax.LanguageService | original-plus-te2-data | TE2 2.28.0 seed; PbiBench V9 | dax-language |
| Fabric auth / REST / OneLake / SQL | PbiBench.Fabric | original-public-api-adapter | MSAL 4.84.2; SqlClient 6.1.6 | fabric |
| DAX Studio query handoff | PbiBench.DaxStudio | external-process-bridge | detected-at-runtime | external-tools |
| Fabric/DataForge/Desktop/VS Code/Codex switcher | PbiBench.App / PbiBench.DaxStudio | original-process-bridge | detected-at-runtime | external-tools |
| SQLBI VPAX parser fixture | PbiBench.Adapters.Tests | licensed-test-fixture | repository Contoso.vpax | fixtures |
| Safe C# Preview / detached recipe execution | PbiBench.Core / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | automation |
| Action recorder / generated C# / macro library | PbiBench.Core / PbiBench.Semantic / PbiBench.App | pbibench-original | 11.1.0 | automation |
| Practical C# language assistance / snippets / recovery | PbiBench.CSharp.LanguageService | pbibench-original | 11.1.0 | csharp-language |
| Provider-neutral AI Context Export | PbiBench.AI.ContextExport / PbiBench.Semantic / PbiBench.App | pbibench-original | 11.1.0 | ai-interchange |
| Separate Fabric Toolbox desktop application | PbiBench.FabricToolbox | pbibench-original | 11.1.0 | fabric |
| Versioned Fabric selection handoff | PbiBench.Core / PbiBench.App | pbibench-original | 11.1.0 | contracts |
| DAX Query workspace / query transport | PbiBench.App / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic |
| DAX scripts / UDF workbench / model metadata authoring | PbiBench.Semantic / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic |
| Calendar / perspective / translation editors | PbiBench.Semantic / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic |
| Relationship diagram / authoring | PbiBench.Semantic / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic |
| Data preview / profiling / Pivot Lab | PbiBench.Core / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | data-exploration |
| Original BPA rule packs / fix review | PbiBench.Automation | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | bpa |
| VertiPaq / VPAX capture / optimization | PbiBench.Core / PbiBench.Semantic / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | quality |
| Semantic assertions / benchmark contracts | PbiBench.Core / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | quality |
| PBIP/TMDL workspace / disk-live-Git sync | PbiBench.Workspace / PbiBench.Git / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | workspace |
| Refresh / reviewed deployment | PbiBench.Core / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic |
| Semantic source import / Direct Lake / schema review | PbiBench.Semantic / PbiBench.App | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | semantic-fabric |
| CLI commands / reviewed local operations | PbiBench.Cli / PbiBench.Automation | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | cli |
| Strict proposal import / preview | PbiBench.Core / PbiBench.Automation | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | optional-agent |
| Optional existing online provider | PbiBench.Automation | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | optional-agent |
| Bounded semantic compiler prototype | PbiBench.Core / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | prototype |
| Local DAX package prototype | PbiBench.Core / PbiBench.Semantic | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | prototype |
| DataForge versioned truth contract reader | PbiBench.DataForge | pbibench-original | baseline 53813388fbf2a3d7075572fd0a33be207faeccdf | dataforge |
| Runtime feature provenance / update ownership | PbiBench.Core / PbiBench.App | pbibench-original | 11.1.0 | provenance |

TE2 local patches: `vendor/patches/te2-2.28.0-remote-write-review.patch` and `vendor/patches/te2-2.28.0-function-undo-order.patch`. Neither was changed in V11.1. Native BPA remains in the TE2 foundation lane.

FastColoredTextBox 2.16.24 is the existing separately packaged editor binary, reused without modification. Its LGPL-3.0 notice and corresponding source provenance are retained in the existing upstream notice/package workflow. See `docs/TE2_LICENSE_INVENTORY_V6.md`, `docs/TE2_NUGET_LICENSE_INVENTORY.json`, and `vendor/notices`.

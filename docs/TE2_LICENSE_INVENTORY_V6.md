# TE2 source and dependency notices

Inspected on 2026-09-04 at official tag 2.28.0, commit `75f10e331b8de0dda5c213180b9b8867b4a38191`. Original files remain in both vendor source trees. No TE3 source, binaries or assets were used.

| Component | License notice supplied by upstream | File in vendor root |
|---|---|---|
| Tabular Editor 2 | MIT, Copyright 2025 Tabular Editor ApS | `LICENSE` |
| FastColoredTextBox | GNU LGPL version 3 | `license-FastColoredTextbox.txt` |
| FastWildcardMatching | MIT, Copyright 2016 H.A. Sullivan | `license-FastWildcardMatching.txt` |
| TreeViewAdv | BSD two-condition notice, Copyright 2009 Andrey Gliznetsov | `license-TreeViewAdv.txt` |
| Combined installer notices | Upstream rich-text license collection | `TabularEditor-license.rtf` |

The FastColoredTextBox notice includes LGPL obligations; TE2's MIT license does not replace dependency licenses. Its original library is retained as a separately available package binary with source provenance. Preserve the supplied notices and source availability when packaging or distributing the application.

`TE2_NOTICE_HASHES.json` records SHA-256 checksums of the five unchanged notice files. `TE2_NUGET_LICENSE_INVENTORY.json` records every restored package's exact version, declared license/URL, and packaged license/notice entry names, extracted directly from the downloaded `.nupkg` metadata. This includes the Antlr runtime/toolchain, Fody/Costura, Windows API Code Pack, ActionListWinForms, DataConnectionDialog, Microsoft TOM/MSAL libraries, Newtonsoft.Json and test dependencies. Legacy URL-only metadata is recorded verbatim rather than assigning an unverified license identifier.

Source-embedded comments were also preserved, including TreeViewAdv's GIF decoder provenance and permission text. PbiBench does not remove attribution from vendored code. The inventories document provenance; deployment packaging must ship applicable notices and retain upstream source separately from generated build output.

The portable packager copies those original notices and extracts license/notice/nuspec files from restored upstream and app packages. `vendor/notices` additionally supplies the declared CPL-1.0 text for ActionListWinForms (whose package omits the text) and the GPL-3.0 text incorporated by the upstream LGPL-3.0 notice, with retrieval provenance. It includes only runtime files and the synthetic public demonstration model; user settings, private models and build/test artifacts are excluded.

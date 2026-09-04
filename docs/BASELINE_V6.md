# TE2 baseline — 2026-09-04

Required contract documents were read before implementation. No TE2 source behavior was changed before the builds and offline tests below.

## Source provenance

* Official upstream: https://github.com/TabularEditor/TabularEditor
* Tag: `2.28.0`; resolved/pinned commit: `75f10e331b8de0dda5c213180b9b8867b4a38191`.
* Checkout: `vendor/TabularEditor2-2.28.0`, fetched using `git clone --branch 2.28.0 --depth 1`.
* Bundled snapshot: `vendor/TabularEditor2-bundled`. Its `VersionHistory.md` ends at 2.27.2 (2025-09-29), but its actual `SharedAssemblyInfo.cs` already declares `AssemblyInformationalVersion("2.28.0")`. Therefore it is an unpinned snapshot with stale version history, not verified 2.27.2 source.
* `scripts/update-te2-2.28.0.ps1` verifies the pinned commit and preserves existing source and local integration patches. It never deletes or overwrites an existing checkout. The generated checkout is ignored by the parent repository; `vendor/patches/te2-2.28.0-remote-write-review.patch` is tracked and applied idempotently after fetching the exact upstream commit.

## Toolchain

Windows; Visual Studio Community / Build Tools 2022 17.12.3. Full-framework MSBuild is at `C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe`. .NET Framework 4.8 reference assemblies are installed. NuGet CLI 6.14.0 was fetched from the official NuGet distribution endpoint into `.tools/nuget.exe`.

The original `global.json` requires .NET SDK 10. System SDKs were 9.0.101 and 9.0 preview. The official dotnet installer installed SDK 10.0.400 into `C:/Users/julia/AppData/Local/PbiBench/dotnet`; no machine SDK was replaced. The reproducible build script can install this user-local SDK using `-InstallSdk`.

Upstream TE2 is .NET Framework 4.8 WinForms with classic packages.config, Antlr generation and Fody/Costura. Pass 1 hosts those working controls in-process from a net48 WPF application. Shared adapters can also target modern .NET; no mechanical TE2 port was attempted.

## Unmodified build results

NuGet restore completed for both snapshots. Commands use `/p:ImportDirectoryBuildProps=false /p:ImportDirectoryBuildTargets=false` to prevent the surrounding PbiBench nullable/deterministic/warnings policy from changing the upstream baseline.

| Source | Configuration | Result |
|---|---|---|
| Bundled | clean Release | Failed: upstream hardcodes `AntlrGrammars/obj/Debug/DAXLexer.cs` in TOMWrapper, absent on clean Release |
| Official 2.28.0 | clean Release | Same pre-existing failure |
| Bundled | Debug, full solution | Passed |
| Official 2.28.0 | Debug, full solution | Passed |
| Official 2.28.0 | Release after Debug grammar generation | Passed |

Warnings include mismatched Microsoft.Identity.Client.NativeInterop reference metadata (0.18.1 vs restored 0.20.2), unused members, deprecated FxCop and missing XML documentation. They remain baseline warnings, not silently altered semantic behavior. Exact build logs are under `artifacts/baseline/`.

Release build ordering workaround is in `scripts/build-pass1.ps1`: build AntlrGrammars in Debug first, then the requested upstream configuration. No source edit is needed for that workaround.

## Unmodified test results

Full VS VSTest console ran the following subsets separately against **both** bundled and pinned source:

| Suite/filter | Pinned 2.28.0 | Bundled |
|---|---|---|
| `TabularEditor.TOMWrapper.GeneratedTests` | 262 passed | 262 passed |
| `ScriptEngineTests`, `ScriptParserTests`, `ScriptHelperTests`, `GetPathTests` | 26 passed | 26 passed |

Total: **288 passed per source**, 576 baseline test executions. The generated TOM tests cover object property mutation, undo and redo. Editor tests cover scripting, tree paths, parser and mock formatter behavior. TRX reports and console logs are in `artifacts/baseline/te2-*.trx` and `.log`.

Live SSAS/Azure deployment and connection tests were not run: they depend on configured servers and some create/replace remote databases. The offline subset requires neither credentials nor remote writes. BPA tests that replace user/machine BPA rule files were not included in this baseline subset. PbiBench has its own scoped BPA/model tests for the product gate.

## Build and integration

Run `./scripts/build-pass1.ps1` from PowerShell; use `-Configuration Release` or `-InstallSdk` when needed. `-SkipTests` and `-SkipUpstreamTests` are explicit development shortcuts. The normal path builds vendor dependencies, the PbiBench solution, product tests and the offline upstream subset. The generated executable is `src/PbiBench.App/bin/<configuration>/net48/PbiBench.exe`.

For an offline fresh workspace, `-Offline` creates the build copy from the bundled source and applies the same minimal patch. Its explicit marker states that this is the supplied snapshot, not a verified official Git checkout. Existing source is preserved. NuGet packages and SDKs still need to be installed/cached before a fully offline build. Online and offline provisioning were exercised in separate `.tools` copies and rerun to check idempotence.

The script prefers a Visual Studio instance with the legacy `Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll`, needed by the upstream editor tests. The installed bare BuildTools instance lacks this DLL; the installed Community instance supplies it. This is a test-toolchain requirement, not a changed TE2 dependency.

The normal build now also launches the compiled PbiBench application with its offline `--smoke-test` fixture and verifies the resulting JSON. `scripts/invoke-smoke-pass1.ps1` uses a fresh evidence directory, a hidden child process, a maximum 60-second timeout, nonzero-exit checking, and explicit startup/smoke-error checks. `-SkipSmoke` is the explicit development shortcut; `-SkipTests` does not silently disable application smoke verification.

After a successful Release build, `./scripts/package-pass1.ps1` prepares `artifacts/PbiBench` as a portable directory, verifies the staged app with the same smoke test, and includes a file SHA-256 manifest plus original and package dependency notices. It never overwrites an existing destination; select another `-Destination` beneath `artifacts` for a later build. Packaging does not create a ZIP or bundle DAX Studio/Power BI Desktop.

The Release portable build was produced and verified on 2026-09-04: 15 packaged-app smoke checks passed; all 382 manifested files matched their SHA-256 values; all five original source notices matched the baseline hashes. The folder contains 383 files including the manifest (39,083,871 bytes), 87 package notice sets and only the synthetic demo BIM. Verification evidence is `artifacts/package-verification.json`; packaged smoke evidence is `artifacts/package-smoke-b5e6ffbc3f8d413798288c173dada0b9/smoke-result.json`.

All upstream license notices are preserved. See `TE2_LICENSE_INVENTORY_V6.md`. The separately documented remote-write review hook is a subsequent minimal integration patch, after the unchanged baseline above.

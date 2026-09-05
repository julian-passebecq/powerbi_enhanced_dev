# Testing delegation — V11.3

Codex during development:
- ModuleCatalog/FeatureCatalog tests;
- Fabric adapter + Toolbox tests;
- C# automation/macro/diagnostic tests;
- focused WPF tests.

Do not rerun the full historical suite after every edit.

Before push run one final relevant Release gate including changed projects, focused net10/net48 tests, Toolbox isolation, relevant WPF/TE2 boundary tests, and package smoke if packaging changed.

After push the separate reviewer will inspect the GitHub diff, module boundaries, accidental cross-module references, CI logs, Fabric read-only behavior, and Safe/Trusted C# boundary.

Do not claim live Fabric/Power BI validation unless a real authenticated target was actually used.

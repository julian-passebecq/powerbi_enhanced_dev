# V11.3 acceptance gate

## Modular architecture
- module_catalog.json validates;
- Feature Map rows resolve module IDs;
- dependency graph acyclic;
- no Freeze lifecycle;
- each implemented module has version/update lane/owner/tests/runtime/process metadata;
- Toolbox forbidden-dependency checks pass.

## Feature Map
- no text says Labs/Companions/Future are permanently frozen;
- lifecycle visible;
- module/update lane shown in details;
- TE3 comparison remains informational/public-doc-only.

## Fabric Toolbox V0.2
- item search/filter and details work;
- inventory export contains no auth token/credential;
- Operations page works;
- recent job/operation list is read-only;
- unsupported types explicit;
- bounded pagination/cancellation;
- no write endpoint on browse/open;
- no TE2/ModelEditor/Semantic assembly loaded.

## C# automation
- Problems panel shows TE2 compiler diagnostics;
- navigation moves caret without execution;
- macro context rules are bounded data;
- incompatible macro disabled with reason;
- old macro files remain readable;
- semantic snippets insert/generate text only;
- Safe/Trusted boundaries unchanged.

## Regression
- V11 recovery/file-conflict tests green;
- AI Context Export tests green;
- feature/provenance/module catalog tests green net10/net48;
- focused WPF tests green;
- Toolbox Release build succeeds;
- relevant Release package/smokes pass.

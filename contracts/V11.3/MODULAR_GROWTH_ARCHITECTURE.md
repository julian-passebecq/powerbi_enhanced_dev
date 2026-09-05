# Modular growth architecture

## Principle

PbiBench may become broad. The rule is not "stop adding features".

> A feature may grow when it has a clear module owner, explicit dependencies, versioned contracts, provenance, update lane and tests.

## Product family

```text
PbiBench Shell / Apps & Tools
├─ Semantic IDE / TE2++             net48, TE2/TOM
├─ Fabric Toolbox                   separate net10.0-windows process
├─ AI Context Export                provider-neutral utility
├─ DAX Studio bridge                external process adapter
├─ DataForge bridge                 versioned JSON contracts
├─ Agent / compiler / packages      PbiBench-owned incubating modules
└─ Future Report/PBIR tooling       own module/sub-app when implemented
```

One visual shell may launch/open modules without runtime coupling.

## Module catalog

Create `docs/architecture/module_catalog.json`.

Each module records:
- id/displayName;
- kind: InProcess | SeparateProcess | ExternalProcess | Library | Lab;
- lifecycle: Active | Selective | Independent | Incubating | OnDemand | Later;
- version;
- entryPoint;
- targetFrameworks;
- ownerProjects;
- updateLane;
- upstreamDependencies;
- contracts;
- dependsOnModules;
- forbiddenDependencies;
- protectingTests.

Feature catalog rows reference `moduleIds`.

## Replace Freeze semantics

Keep maturity status: Core / Companion / External / Utility / Labs / Future / Gap.

Use development lifecycle:
- Active
- Selective
- Independent
- Incubating
- On demand
- Later

Suggested mapping:
- Semantic IDE: Active
- DAX IDE/model authoring: Active
- C# automation: Active or Selective
- Fabric semantic integration: Active
- Fabric Toolbox: Independent
- AI Context Export: Selective
- DAX Studio bridge: On demand
- DataForge integration: On demand
- Embedded Agent: Incubating
- Semantic compiler: Incubating
- DAX packages: Incubating
- PBIR/report engineering: Later
- Knowledge/tutorial: On demand
- DAX debugger: Later

None prohibit future development.

## Dependency rules

Semantic IDE may depend on net48-compatible Core/Semantic/ModelEditor/Automation/DAX/PBIP/Git/Fabric contracts.
It must not depend directly on FabricToolbox UI/executable, future ReportToolbox UI, or DataForge application UI.

Fabric Toolbox may depend on Core contracts and PbiBench.Fabric. It must not load/reference TabularEditor, TOMWrapper, PbiBench.App, PbiBench.ModelEditor, or Semantic UI/runtime.

Cross-module communication prefers versioned JSON handoffs or immutable DTO contracts:
`.pbifabric.json`, DataForge contracts, AI context ZIP, future report-selection handoffs.

No shared mutable singleton state across sub-apps.

## Update lanes

TE2 update -> Semantic IDE lane only.
Fabric APIs/MSAL/SqlClient -> Fabric lane + Toolbox + semantic-Fabric contract tests.
DAX grammar/function metadata -> DAX language lane.
DataForge contract change -> bridge compatibility tests.

Do not opportunistically upgrade unrelated upstreams in the same lane.

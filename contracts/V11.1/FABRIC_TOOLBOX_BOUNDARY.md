# Fabric Toolbox boundary

## Decision

Create a separate Fabric Toolbox application, but do it incrementally.

The existing `PbiBench.Fabric` library already dual-targets `net10.0;net48` and references Microsoft Identity Client and Microsoft.Data.SqlClient. Reuse this service layer; do not fork it.

## Semantic IDE retains

Only Fabric capabilities needed directly to author/test/deploy a semantic model:
- choose/connect workspace semantic model
- source-table selection/import for semantic authoring
- Direct Lake semantic-model validation/conversion guards
- schema comparison relevant to the current model
- semantic model refresh/deploy
- live model target selection

## Fabric Toolbox owns

Broad Fabric platform workflows:
- workspace explorer
- complete item/resource inventory
- OneLake explorer
- lakehouse explorer
- warehouse/SQL endpoint explorer
- shortcuts and source inspection
- pipeline/job execution history and monitoring
- notebook/item inventory
- deployment pipeline/platform operations
- capacity/tenant diagnostics where permissions allow
- broader governance/security inventory
- diagnostics that do not require a semantic model to be open

## Do not duplicate

`PbiBench.Fabric` remains the shared service/adaptor library.

Fabric Toolbox should not reference:
- `PbiBench.ModelEditor`
- TE2 UI assemblies
- hosted `FormMain`
- PbiBench Semantic IDE WPF views

Semantic IDE should not absorb Fabric Toolbox view models just to reuse a screen.

## First implementation pass

1. Add `src/PbiBench.FabricToolbox/PbiBench.FabricToolbox.csproj` as a modern .NET desktop executable.
2. Create a minimal shell: Home, Workspaces, OneLake/Data, Operations, Settings/About.
3. Reuse existing Fabric catalog/auth/SQL services.
4. Add "Open Fabric Toolbox" from Semantic IDE App Switcher.
5. Add versioned selection handoff back to Semantic IDE, but no credentials.
6. Keep old Semantic IDE Fabric page as compatibility surface until equivalent broad features are proven in the toolbox.
7. Migrate broad features one by one in later passes; delete duplicates only after acceptance.

## Why separate process

- TE2 host stays on stable net48.
- Fabric libraries/API clients can move to modern .NET independently.
- MSAL/SqlClient updates do not threaten TE2 assembly loading.
- Fabric API changes get their own test/release lane.
- Crash/auth state is isolated.


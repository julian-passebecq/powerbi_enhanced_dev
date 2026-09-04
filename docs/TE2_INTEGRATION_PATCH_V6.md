# Minimal TE2 integration patch

The unchanged baseline in `BASELINE_V6.md` was built and tested first. Pinned commit remains `75f10e331b8de0dda5c213180b9b8867b4a38191`; integration changes are stored in tracked `vendor/patches/te2-2.28.0-remote-write-review.patch` and applied to the ignored build checkout by the safe update script. Repeated provisioning preserves and verifies the applied patch.

## Remote-write review seam

`TabularModelHandler.ReviewRemoteWrite` and `TabularDeployer.ReviewRemoteWrite` are optional instance callbacks of type `Func<string, string, string, bool>`. Parameters are the operation, target and proposed database JSON or actual deployment TMSL. Null preserves the upstream behavior; returning false throws `OperationCanceledException` before mutation.

* `SaveDB()` invokes review before reconnecting, suspending undo, updating the database or saving model changes.
* `Deploy()` generates its normal TMSL and reviews that exact TMSL immediately before execution. A rejected plan is neither executed nor assigned to `LastExecutedTmsl`.
* Static `Deploy(handler, ...)` overloads reuse `handler.TabularDeployer`, preserving the host's configured review callback.
* Targets use server name/database name. Connection strings are not passed into the review UI or logs. Proposed metadata may contain model data-source information and is for explicit local review, not logging.
* No global locator or cloud dependencies were introduced. Trusted arbitrary C# scripts are not a sandbox; code that directly constructs another TOM server/deployer can bypass host services, and should be treated as trusted code.

The host connects these instance callbacks to its ApprovedChangePlan review workflow. This patch alone does not claim live remote connectivity has been verified.

## Verification

Both Debug and Release build after the patch. Four added offline tests verify rejected deployment cannot execute, approved deployment receives exact context before execution, null callback retains original execution, and declined SaveDB stops before connecting while retaining undo. The patched TOM run passed **266/266** tests (262 upstream plus 4 new). Evidence: `artifacts/baseline/te2-review-hooks.tom.trx`.

Modified upstream files: `TOMWrapper/TOMWrapper/TabularModelHandler.Database.cs`, `TOMWrapper/Utils/TabularDeployer.cs`, `TOMWrapperTest/TOMWrapperTest.csproj`, and added `TOMWrapperTest/RemoteWriteReviewTests.cs`. Upstream license and notice files are unchanged.

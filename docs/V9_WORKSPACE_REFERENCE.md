# V9.5 workspace synchronization

The Workspace page retains the existing PBIP/Git experience and adds a synchronization view. PbiBench remains the main application, the current TE2-derived editor remains the loaded model owner, and public TOM/TMDL/XMLA interfaces provide the additional serialization and transport. No second `TabularModelHandler` is constructed.

## States and comparison

Configure the view with a PBIP project containing one semantic model, a semantic-model folder, a TMDL `definition` folder, or `model.bim`. Projects containing multiple semantic models require choosing a specific folder. The view displays:

- **Disk**: a captured set of definition files, its content hash and watcher sequence.
- **Live**: fresh metadata from a separately connected XMLA/TOM session, identified by its resolved database name and ID.
- **Loaded editor**: the current TE2 model, with native `HasUnsavedChanges` status. Its metadata can differ from both Disk and actual Live.
- **Git**: an independently parsed HEAD definition and its semantic changes against Disk.
- **Baseline**: the last saved synchronization state, Git HEAD on the first comparison where available, or an explicitly labeled initial disk baseline.

Metadata objects use their lineage tag where available and otherwise a conservative type/parent/name path. Properties are compared independently; named object collection order does not create a change, while ordered unnamed values retain order. Database transport name/ID do not contribute to semantic equality. Model metadata and compatibility level do. A rename with stable lineage appears as a name change; without stable identity it may appear as a deletion and addition.

The three-way grid distinguishes Disk-only, Live-only, identical and conflicting changes. Deleting an object while the other side modifies its properties produces a conflict. A separate **Git semantic diff** tab always shows HEAD-to-Disk property rows, including UDF expressions, even after a newer synchronization baseline is saved. Selecting a row reveals its full before/after text. Credential-related values are masked in displayed plans; the underlying recovery metadata is treated as a model artifact.

Git reading is read-only: `rev-parse` verifies HEAD, `ls-tree` enumerates the pinned commit, and `cat-file` reads validated blob hashes. It never checks out, stages, resets or applies a Git change. Missing commits, malformed definitions, linked objects and unreadable Git results are reported as unavailable rather than clean.

## Watchers and concurrency

The disposable file watcher debounces relevant changes and increments an invalidation sequence. Rename/delete events and watcher overflow invalidate the captured state. It never reloads the native model, changes a file or automatically approves synchronization. A comparison whose file sequence or configured model changes during capture is discarded.

File-content notifications and directory-name notifications are monitored separately. Windows directory timestamp notifications from read-only traversal do not invalidate a comparison; actual model-file edits and directory creation, rename or deletion still do. The most recent accepted notification is available in the diagnostic status. Late callbacks from a disposed or replaced watcher cannot change the new workspace, and a delayed notification already incorporated in the comparison's disk sequence does not discard that comparison.

Live wrapper metadata is captured on the model-owning UI thread. Captured disk files are parsed on the shared background queue through detached public TOM objects; TOM never reparses a later, possibly changed disk version in place. XMLA operations own their connections and sessions. They never call the host model's `SaveChanges` or reuse its session ID. Authentication information is transient and excluded from workspace connection serialization and display.

## Pull Live → Disk

Pull uses the freshly captured actual Live metadata, not unsaved editor metadata. The preview shows the complete destination metadata differences. It replaces the selected semantic definition with the selected source; it is not an automatic merge. Divergent changes require an explicit source-resolution checkbox and then approval of the complete review.

After review, Pull captures Live again and verifies its database ID and semantic fingerprint. The disk writer verifies the original file snapshot and every changed file's expected content under exclusive file access. Before changing anything it creates a complete semantic definition backup and manifest under the definition folder's `.pbibench/workspace-backups/<plan-id>` directory. These are local recovery artifacts, not additional semantic files to deploy.

Only `model.bim`, or the captured/generated `.tmdl` set, is changed. PBIR, `DAXQueries`, `.pbi`, `.git`, other `.pbibench` content and unknown file types remain untouched. Known obsolete TMDL files appear in the plan and are removed only after their expected contents are verified. The writer does not delete a destination file to work around a failed replacement.

On Windows, existing files are locked during verification and writing. Deletion marks the validated, exclusively held file handle using the public `SetFileInformationByHandle` API, avoiding a close/delete race with another process replacing the same path. If a commit fails or is canceled, completed files are restored in reverse order only if their contents still match this operation's writes. Newer external changes are preserved and the error reports the recovery backup path. A multi-file definition is not an atomic filesystem transaction; external editors should wait until the operation completes. The backup/manifest remains available after completion or failure.

Loaded editor drafts remain unchanged after Pull. Reloading or saving those drafts is a separate decision.

## Push Disk → Live

Push uses a typed, immutable preview bound to the disk hash, live fingerprint, resolved target and exact TMSL. It requires an `ApprovedChangePlan` for `RemoteModelWrite`, rejects reused/foreign plans and validates the approval time. Plans expire after 30 minutes. A model compatibility level change is rejected rather than silently upgrading or downgrading the live model.

Unsaved native editor changes block Push. If a connected editor is clean but differs from actual Live, it must be reloaded before Push. A clean offline disk model can be pushed to a selected live connection. These checks keep the loaded draft distinct from the source definition and the destination database.

The private session begins an XMLA transaction, re-captures the destination and verifies both database identity and semantic hash, writes a fresh BIM recovery snapshot, rechecks Disk, and executes the reviewed `CreateOrReplace` command. TMSL `object.database` uses the existing database **name**; the replacement database definition retains the resolved **name and ID**. Both are tested with different values. The operation commits once and captures the resulting Live metadata.

This is a full metadata replacement. Omitted objects can be deleted, including roles and partitions; the preview makes those destination changes visible. Credentials missing from the model definition are not reconstructed. Changed partitions can require refresh. A BIM snapshot can restore metadata but cannot restore processed data. The endpoint's permissions and supported metadata remain authoritative.

Failure attempts transaction rollback. A failed or interrupted commit can have an uncertain outcome, so the UI reports the need to reconnect and compare before retrying. After a remote command has actually been dispatched, the view raises `RemoteWriteCompleted` on the UI thread for both success and later failure, with the captured `LastExecutionOwner`; the shell can require reloading the stale editor before any subsequent native write. A failure in preflight does not raise that event.

The completion notification also exposes immutable `LastExecutionConnection` and the resolved `LastExecutionDatabaseId`, so the shell can invalidate a newly reconnected editor for the same target. Both Pull and Push capture the original baseline store before dispatch. A completed operation persists to that store even if the user changed folders or models while it ran, but it updates or refreshes the visible workspace only while the original generation and handler still match.

Cancellation calls `CancelCommand` on the private session. It is cooperative, including bounded connection/operation timeouts, and cannot guarantee that a just-committed remote operation was undone. Snapshot recovery files are stored under the active profile's `WorkspaceRecovery` directory. Per-root-and-target baselines are under `WorkspaceBaselines`. Passing an isolated `settingsDirectory` keeps acceptance runs separate from the normal `%LOCALAPPDATA%\PbiBench` profile.

## Format and bounds

The pinned Microsoft serializer was exercised with a compatibility-1702 model containing a DAX UDF. It emits **`functions.tmdl`** and round-trips the function metadata without a semantic hash change. This pass uses that verified public layout instead of inventing a per-function folder convention. Existing PBIP `DAXQueries/[Tab].dax` support remains available through the DAX workspace.

Definition capture is limited to 10,000 files, 16 MiB per file and 64 MiB total. Files are opened with sharing that prevents concurrent writes while their bounded contents are read; a size change during capture is rejected. Semantic snapshots allow up to 64 MiB JSON characters, depth 100 and 300,000 metadata properties. Git blobs use the same file/total bounds. Linked paths, rooted relative paths, traversal, alternate streams, reserved Windows device names and preserved cache/query paths are rejected for writes.

## Public interfaces and validation

- Microsoft's [TMDL overview](https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview) and [TMDL getting-started guide](https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-how-to) define the public metadata format and serialization API.
- Microsoft's [PBIP semantic-model folder reference](https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-dataset) documents the model definition and DAX query artifacts.
- Microsoft's [CreateOrReplace reference](https://learn.microsoft.com/en-us/analysis-services/tmsl/createorreplace-command-tmsl) explains replacement/deletion behavior, and [XMLA transaction guidance](https://learn.microsoft.com/en-us/analysis-services/multidimensional-models-scripting-language-assl-xmla/managing-transactions-xmla) describes the session transaction boundary.
- The existing pinned MIT TE2 deployment path calls `Server.Execute(tmsl)` directly. The new transport uses the same public API. An offline pinned-SDK test sets `CaptureXml = true`, executes the raw generated command, checks the capture, and verifies that `Connected` remains false. No live catalog is needed for that protocol-shape regression.
- Microsoft's [FILE_DISPOSITION_INFO reference](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_disposition_info) supplies the public Windows handle-deletion contract.

Focused results on 2026-09-05: **26 `WorkspaceSyncTests` pass on net10.0 and 26 on net48; 8 `WorkspaceNativeTests` and 8 `WorkspaceSyncViewTests` pass on net48**. Coverage includes three-way conflicts, lineage renames, complete deletion conflicts, hash normalization, guarded file replacement/deletion, artifact preservation, backup recovery, cancellation, concurrent replay/newer edits, unsafe paths, watcher invalidation and ignored directory timestamp noise, bound baselines, pinned Git blobs, TMDL/UDF round-trip, independent handler preservation, approval/stale-session guards, target-name/ID TMSL, rollback and offline raw-TMSL capture. WPF checks exercise the first read-only TMDL comparison, old-watcher callbacks, delayed incorporated notifications, real edits during capture, and completion persistence after changing the target. TRX evidence is under `artifacts/v95-workspace-tests`.

The private-session tests use deterministic transport fixtures; this evidence does not claim a live tenant deployment or authenticated XMLA write. The full V9.5 gate separately records the application build, wider regressions and launch/UI checks.

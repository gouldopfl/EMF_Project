# ADR-047 Milestone 3 Implementation Handoff

This handoff describes the uncommitted implementation of explicitly invoked immutable generation reclamation in EMF.Persistence. It is intended for the next developer and reviewer. The final precertification correction enforces a 100,000-entry per-call inspection ceiling, creates the live namespace watch through the platform boundary, and uses one continuation model. Total namespace size has no policy ceiling. This revision has focused validation only; the earlier full-suite run is not evidence for the corrected implementation.

## Authority and repository state

- Date: 2026-10-02.
- Request: `/home/michael/EMF_Input/ADR-047 MILESTONE 3 — FINAL PRE-CERTIFICATION CORRECTIONS.pdf`, preserving the previously accepted watch and continuation invariants.
- Architecture: [ADR-047](../architecture/ADR-047-versioned-artifact-content-mutation.md).
- Repository: `/home/michael/EMF_Project`.
- Branch: `main`.
- HEAD and `origin/main`: `3861d4f3ad618f35023953bb6603c87860bb4cdd`, Implement ADR-047 legacy content migration.
- Starting working tree was clean. Implementation changes remain uncommitted. Nothing was pushed.
- Recovery backup: `/home/michael/GitBackups/EMF_Project.git(20261002-180904).zip`.
- Verified backup SHA-256: `9af9b7acab33be991bd34bbca66c27cc813e1409099e8cbacb0ed538290e087d`.

## Files changed

| File | Purpose |
| --- | --- |
| `src/EMF.Persistence/Storage/FileSystemArtifactContentGarbageCollector.cs` | Public provider-specific collector, options and result records, plus private store implementation sharing existing validation helpers. |
| `src/EMF.Persistence/Storage/FileSystemArtifactContentStore.cs` | Makes the store partial, allows the collector’s private store to defer namespace enumeration to bounded inspection, and allows validation within a connection-owned SQL transaction. Ordinary stores retain their existing streaming namespace enumeration. |
| `src/EMF.Persistence/Storage/MigrationOrigins.cs` | Allows provenance validation within the collector's connection-owned SQL transaction. |
| `src/EMF.Persistence/Storage/IGenerationNamespaceWatch.cs` | Internal watch semantics shared by the GC algorithm and platform implementations. |
| `src/EMF.Persistence/Storage/IContentStoragePlatform.cs` | Factory for generation namespace watches at the existing OS boundary. |
| `src/EMF.Persistence/Storage/LinuxContentDurability.cs` | Creates the Linux watch after supported-platform preflight. |
| `src/EMF.Persistence/Storage/LinuxGenerationNamespaceWatch.cs` | Synchronous nonblocking inotify validation of the live namespace epoch. |
| `tests/EMF.Tests/ArtifactContentGarbageCollectionBoundedInventoryTests.cs` | Bounded inspection, mutation, overflow, lifecycle and large-namespace tests. |
| `tests/EMF.Tests/ArtifactContentGarbageCollectionTests.cs` | Synthetic-root safety, concurrency, retry, migration and process-kill tests; token-only continuation. |
| Existing versioned-store and migration test platform fakes | Forward or explicitly reject the new watch capability. |

No Core GC contract, schema migration, application startup hook or automatic reclamation was added. The internal platform interface gains a watch factory; existing Linux durability supplies the Linux watch. No Windows implementation was added.

## Authoritative eligibility inventory

| Physical category | Treatment |
| --- | --- |
| Current generation referenced by `ContentState` | Protected. |
| Historical generation recorded by `ContentReceipts.PublishedGeneration` | Potentially reclaimable when neither current state nor a migration origin protects it. Receipt rows and lineage remain unchanged. |
| Durable candidate never promoted into current state | Potentially reclaimable after exclusive generation coordination establishes that producers have released their shared gates. |
| Partial candidate named with the provider's `.tmp` suffix | Potentially reclaimable under the same coordination and entry checks. |
| Generation referenced by `ContentMigrationArtifacts` | Protected even after replacement or tombstoning; admission still requires its migration byte evidence. |
| Retained legacy files, sibling migration workspace, manifests, checkpoints and publication intents | Outside generation GC; preserved. |
| Unknown names, directories, symlinks, hard links and special files in the generation namespace | Fail closed before batch unlink. |

The live durable reference inventory is current state, historical receipt generation identities and migration origins. Migration recovery state is validated through the existing completion and provenance routines. There is no new provenance-retention transition or GC journal.

## Coordination and durability

The collector first performs normal store admission. It then acquires the stable generation gate exclusively and opens the catalog while holding the gate. It explicitly issues `BEGIN EXCLUSIVE`, waits boundedly for active SQLite readers to clear, and blocks new catalog readers and writers during eligibility validation and reclamation.

Raw SQL is deliberate: the managed non-deferred transaction API issues `BEGIN IMMEDIATE`, which alone does not establish the required reader exclusion. Validation commands share the same connection-owned exclusive transaction.

Under both forms of coordination, GC rechecks structure, migration completion, schema, catalog integrity, current generations and lineage. It also revalidates migration byte evidence on each invocation. It builds the protected set anew from current state and migration origins on every call. Before each actual unlink it queries both authoritative tables again on the exclusive SQLite connection, verifies the change watch is clean, and checks the entry’s ownership, permissions and identity. Watch cleanliness alone never authorizes deletion.

GC flushes the generation directory before committing the empty catalog coordination transaction and releasing the generation gate. A no-op retry also flushes the directory, allowing a retry after an earlier unlink with an unacknowledged durability barrier. No catalog state, revision, receipt, tombstone or provenance row is changed by reclamation.

Readers retain their existing catalog read transaction through the complete payload copy. Producers retain their existing shared generation gate from before candidate creation through mutation outcome resolution. Neither protocol was weakened.

## Bounded validation and reclamation

A sweep calls `IContentStoragePlatform.CreateGenerationNamespaceWatch` **before** obtaining its lazy namespace enumerator or inspecting the first entry. Linux supplies a nonblocking inotify implementation. That same watch, enumerator and accumulated identities stay owned by the collector across calls. Validation consumes at most `MaxNamespaceEntries` entries per call, and accepts only budgets from 1 through 100,000 inclusive. `FileSystemArtifactContentGarbageCollector.MaximumNamespaceEntriesPerCall` owns the enforced maximum and supplies the unchanged default. It checks exact lowercase provider names, permissions, ordinary-file type, symlinks and hard links. It neither restarts enumeration to skip a prefix nor sorts the complete inventory in one call. Validated entries enter an ordinal priority queue incrementally.

No entry is reclaimed until enumeration reaches its end and the watch confirms no queued invalidating event. Reaching an exact inspection-budget boundary may require an additional call to establish end-of-enumeration. Small namespaces below the budget can validate and reclaim in one call.

Each call holds the exclusive generation gate and SQLite `BEGIN EXCLUSIVE`; both are released before returning a continuation. The watch remains active between calls. Catalog, lineage, current state, migration completion and retained evidence are rechecked under coordination on the next call. These established authoritative checks remain complete: the namespace-entry budget is not a total catalog, evidence-byte or elapsed-time budget.

Any queued modify, attribute, create, delete, rename, directory move/delete, unmount, ignored-watch or overflow event invalidates accumulated validation. At the beginning of a resumed call, a detected change discards the sweep and starts a new watch and bounded scan, returning a different token and `ValidationRestarted=true`. A change discovered during validation or before unlink throws and discards the session; restart explicitly without a token. Watch creation/read/handle failures also fail closed. Overflow is never silently drained to preserve an old epoch.

The collector verifies the watch after each validation batch, before reclamation, immediately before unlink and before directory flush. Its own deletion is acknowledged only by exactly one matching `IN_DELETE` event; any additional or unexpected event aborts the sweep. Unknown or unsafe entries in any validation batch prevent all reclamation for that sweep. Physical deletions from a previously completed validation epoch cannot be rolled back if a subsequent external change or failure occurs.

The change watch complements exclusive provider coordination and the existing private-root and immutable-file assumptions. It never replaces catalog or provenance authority. There is no durable GC journal, persisted deletion permission or age-based eligibility.

## Platform boundary

`IGenerationNamespaceWatch` is internal to EMF.Persistence and defines only `Changed`, `RequireUnchanged`, `AcknowledgeOwnDeletion` and disposal. `IContentStoragePlatform.CreateGenerationNamespaceWatch(directory)` returns that interface. `GenerationSweep` holds only the interface and obtains it through the injected platform. The GC algorithm contains no Linux type, inotify API, libc import or Linux event constant.

`LinuxContentDurability` constructs `LinuxGenerationNamespaceWatch`; native calls and event interpretation remain in that Linux implementation. A future Windows platform can supply the same semantics without redesigning the collector. Unsupported watch creation fails before any namespace inspection or unlink.

Focused tests inject a platform returning an observable wrapper watch. They verify factory invocation before validation, the same watch across continuation, use of change/deletion checks, and disposal at completion. Watch overflow and failure injection are owned by that test platform, rather than by the shared collector.

## AfterName decision

`AfterName` was removed from the options, result, sweep state, validation and tests. The original Milestone 3 bounded-operation requirements ask for progress and protection against starvation, but contain no independent requirement for a caller-selected ordinal lower bound. The live sweep token and scheduling queue satisfy those requirements. The API is uncommitted, so retaining a second cursor solely for compatibility would add ambiguity without a required use case.

## API and continuation semantics

Use `FileSystemArtifactContentGarbageCollector.CollectAsync` explicitly and dispose the collector when the sweep is complete or abandoned.

| Option | Default | Meaning |
| --- | --- | --- |
| `MaxNamespaceEntries` | 100,000 | 1 through 100,000 inclusive per call; no policy ceiling on total sweep size. |
| `MaxEntriesSelected` | 1,000 | 1 through 10,000 entries removed from the validated scheduling queue per call, including protected entries. |
| `MaxFilesReclaimed` | 100 | 1 through 10,000 actual physical unlinks per call. |
| `Continuation` | null | Opaque token bound to this live collector’s specific sweep, enumerator and kernel watch. |

When `SweepComplete=false`, pass the returned `Continuation` into the next call on the **same collector instance**. A null continuation intentionally starts a fresh sweep. An unknown, expired, disposed or foreign token throws before reclamation. Tokens cannot be reused after process restart, another collector instance, completion, failure or invalidation. Replaying the token only schedules more work in its live session and never supplies independent deletion authority.

`NamespaceEntries` reports the accumulated discovered count for this sweep. `EntriesInspected` reports this call’s namespace validation count. `ValidationComplete` distinguishes inspection from reclamation. `ValidationRestarted` reports automatic replacement of an invalidated sweep. Selected entries, reclaimed files and reclaimed bytes are per-call counts.

Protected entries are popped and advance the queue, so a protected prefix spanning many selection batches cannot starve a later eligible candidate. After complete validation, reclamation continuations inspect no additional namespace entries. Every invocation still rechecks authority under both locks, and every unlink executes the fresh current-state and migration-origin query.

The tested namespace of 100,002 entries completes with the unchanged default inspection budget. There is no policy ceiling on total namespace size. Accumulated scheduling and identity state uses memory proportional to the namespace; a restart loses that state and begins validation again. Operating-system resource exhaustion fails closed rather than authorizing a partial validation view.

Gate acquisition and concurrent invocation waits use ten-second bounds. Catalog connections retain the existing two-second SQLite busy timeout. Disposal serializes with an active call; do not invoke disposal from a checkpoint callback.

## Failure and retry behavior

Before the first unlink, validation failure leaves generation files unchanged. An exception or process death after unlink may leave some eligible files absent, but catalog logical state remains unchanged. A retry starts fresh validation, rechecks authoritative state, tolerates obsolete payloads already absent from the new namespace view, and flushes the directory again.

Directory-flush failure does not produce a successful result. Cancellation or a fault after physical deletion also does not guarantee that no files changed. Returned counts describe successful calls only; a thrown call may have performed a partial physical batch. OS and SQLite coordination recover after process death without deleting or replacing the stable gate.

## Scope and remaining work

Migration-origin reclamation requires a later explicit provenance-retention transition. Do not weaken Milestone 2 evidence validation to reclaim those generations.

Remaining writer migrations, rewrapping conversion, authenticated rewrap, ADR-048 ingestion recovery, ADR-049 audit delivery, ADR-028 audit-chain changes, backup revision fencing, Windows storage, broad retention, retained legacy deletion and migration-workspace cleanup remain outside this milestone.

Tests used synthetic temporary roots. No real artifact-content root, OSA, GERD or Lumbar data/packages were accessed, and no live Azure operations were run. Process-kill tests demonstrate coordination recovery and physical retry behavior; they do not establish power-loss durability. Supported filesystem, SQLite and hardware flush assumptions still apply.

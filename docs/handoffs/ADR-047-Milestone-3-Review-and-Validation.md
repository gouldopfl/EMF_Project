# ADR-047 Milestone 3 Review and Validation

This document gives the reviewer the recorded validation results and the checks needed to assess the uncommitted GC implementation. Read it with the [implementation handoff](ADR-047-Milestone-3-Implementation-Handoff.md), the final precertification PDF and ADR-047.

## Recorded validation

Validation was performed on 2026-10-02 against the uncommitted implementation on `main` at `3861d4f3ad618f35023953bb6603c87860bb4cdd`.

| Check | Recorded result |
| --- | --- |
| Final combined focused run | 249 passed, zero failed, zero skipped; 54 seconds. Receipt: `focused-tests.trx`. |
| `git diff --check` | Passed; new untracked files also checked with `git diff --no-index --check`. |
| Final GC focused run | 57 passed, zero failed, zero skipped: 30 bounded-inventory cases and 27 existing GC cases. |
| Storage and migration focused run | 192 passed, zero failed, zero skipped. |
| ArchitectureAuditor | Zero parse errors; all eight architecture rules pass. Sixteen resource findings require separate review; no finding targets the new GC or watch files. |

The preceding implementation’s full-suite result is historical and does not validate this correction. **No full regression, commit or push was performed for this correction.** Azure OpenAI remained disabled and all storage tests used synthetic temporary roots.

Fresh final combined focused results and the complete diff are recorded in the correction evidence package under `/home/michael/EMF_Output/ADR-047-milestone3-precertification/`.

## Test coverage

The new tests cover:

- Bounded batches and live sweep-token continuation, preservation of current bytes/revision and historical receipts, tombstone reclamation and recreation.
- Faults before unlink, after one unlink and before directory flush; safe retry without catalog changes.
- A live producer paused at `CandidateDurable`; GC waits until outcome resolution releases the shared gate.
- A selected reader completing its copy while GC waits for SQLite EXCLUSIVE coordination; new readers remain blocked during the exclusive window.
- A mutation completing before exclusive recheck; its newly current generation is protected.
- Durable orphan and partial temporary candidates after failed preparation.
- Unknown entries, directories, symlinks, unsafe permissions, FIFOs, hard links, missing current payloads and unsupported schema versions.
- Protected migration-origin generations after later writes and admission by a new store instance.
- Incomplete migration, damaged provenance and altered retained evidence.
- Real process termination before unlink, after unlink, before directory flush and while a producer holds a durable candidate.
- Directory-flush failure and a subsequent no-op retry that repeats the flush.
- Invalid per-call budgets (zero, negative, maximum plus one, one million and `int.MaxValue`) rejected before inspection or deletion; maximum 100,000 accepted and equal to the default.
- An injected platform supplying a wrapper watch used before validation and throughout continuation, and an unsupported watch factory failing before reclamation.
- A 100,002-entry namespace validating in multiple calls with the default 100,000-entry budget and eventually reaching `SweepComplete`.
- Protected entries across many bounded validation and selection batches, with eventual reclamation of the later ordinal candidate.
- Unsafe permissions in a later validation batch preventing every unlink in that sweep.
- Create, delete, rename and metadata events during validation invalidating the sweep; a transient create/delete immediately after watch establishment proves the watch precedes enumeration.
- Mutation between calls replacing the token and restarting bounded validation; mutation after complete validation preventing stale unlink.
- Simulated kernel queue overflow both during inspection and after validation; no stale deletion authority survives. This injects the `IN_Q_OVERFLOW` event into the synchronous event-reading path rather than changing host-wide inotify limits.
- Real directory move, replacement, deletion and recreation; simulated watch-handle loss; old continuation rejection after invalidation.
- A foreign live collector, collector disposal, and actual worker-process death all preventing reuse of saved tokens.
- Fresh protection-query execution per actual unlink and preservation of a newly current generation after mutation between reclamation calls.
- Efficient one-call completion for ordinary small namespaces and preserved existing concurrency, damage, migration and retry behavior.

The process worker only runs when given a controlled synthetic root and matching authorization marker by its parent test. It is not an application entry point.

## Reproduce validation

Run from `/home/michael/EMF_Project`. These commands exercise tests, not an operator GC command against an existing content root.

Focused checks:

```bash
EMF_AZURE_OPENAI_LIVE=false \
EMF_AZURE_OPENAI_LIVE_TESTS=false \
dotnet test tests/EMF.Tests/EMF.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~ArtifactContentGarbageCollectionTests|FullyQualifiedName~VersionedArtifactContentStoreTests|FullyQualifiedName~ArtifactContentMigration' \
  -v minimal
```

Do not run full regression yet. Reproduce the focused filters in `evidence.txt` in the correction package.

Review working-tree formatting and checkpoint:

```bash
git diff --check
git status --short
git branch --show-current
git rev-parse HEAD origin/main
```

The original instruction prohibits committing or pushing. Preserve that restriction unless the user gives a subsequent instruction changing it. Do not reset, clean, stash or discard the implementation to reconstruct the starting state.

## Synthetic usage example

This example creates a new temporary root and demonstrates one bounded GC batch. It does not select any real application content root.

```csharp
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

var root = Path.Combine(Path.GetTempPath(), "emf-gc-demo-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new FileSystemArtifactContentStore(root);
    var id = new ArtifactId("synthetic");
    await store.WriteAsync(id, new byte[] { 1 });
    await store.WriteAsync(id, new byte[] { 2 });

    using var collector = new FileSystemArtifactContentGarbageCollector(root);
    var result = await collector.CollectAsync(new(
        MaxNamespaceEntries: 100,
        MaxEntriesSelected: 10,
        MaxFilesReclaimed: 1));

    while (!result.SweepComplete)
    {
        result = await collector.CollectAsync(new(
            MaxNamespaceEntries: 100,
            MaxEntriesSelected: 10,
            MaxFilesReclaimed: 1,
            Continuation: result.Continuation));
    }
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}
```

The example requires the supported Linux local filesystem environment. A collector call is destructive for eligible physical files; no real-root GC operation was performed during implementation or handoff preparation.

## Reviewer priorities

1. Confirm watch creation occurs through the internal platform abstraction before enumeration, and budgets are enforced at 1 through 100,000 per call. Confirm the lock order: normal admission, exclusive stable generation gate, SQLite `BEGIN EXCLUSIVE`, authoritative recheck, unlink, generation-directory flush, catalog coordination release, gate release.
2. Confirm the raw SQLite transaction encloses all eligibility reads and remains active through directory durability acknowledgement. Nullable managed transaction arguments here denote participation through the same connection-owned transaction.
3. Confirm `ContentState` and migration-origin references protect files, while historical receipts and lineage survive obsolete physical reclamation.
4. Review whole-namespace prevalidation and exact lowercase provider naming. Unknown or unsafe entries must stop destruction before batch unlink.
5. Confirm namespace inspection is capped per call, preserves its enumerator across a live watch session, and restarts or fails closed on any event or uncertain coverage. Catalog lineage and migration evidence checks retain their full authority and do not have a total elapsed-time budget.
6. Confirm failures after physical unlink never imply rollback of physical deletion or acknowledged durability. Retry must rebuild authority and repeat the directory flush.
7. Confirm no GC call has been added to startup, construction, reads, mutations or migration admission.
8. Keep migration-origin retention and all excluded milestones separate. Passing tests are implementation evidence, not an independent architecture approval or power-loss certification.

## Handoff state

Implementation files and these handoff documents remain uncommitted. HEAD and `origin/main` remain at the approved checkpoint. The implementation handoff lists the code files; this document records verification and review tasks. No deployment, real-root GC, commit or push is part of this handoff.

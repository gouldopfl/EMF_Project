# ADR-047: Versioned Artifact Content Mutation

## Status

Accepted

## Date

2026-10-01

## Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

The bounded ADR-047 protocol-identity rules apply to its new operation, physical revision, ownership, audit-event and receipt/enumeration identities. They do not redefine the pre-existing ArtifactId contract: ArtifactId remains nonblank, rejects control characters, allows Unicode and has no new provider-specific length ceiling in Milestone 1. In the versioned content store it is logical catalog/resource data, not a generation filename; admission and receipt decoding use its authoritative Core constructor semantics without ArtifactId-derived filesystem paths or obsolete flat-file filename restrictions. Path safety applies to provider-owned root/protocol paths and generated immutable-generation names. Any future global ArtifactId length bound or syntax hardening requires a separate architecture decision with compatibility and persisted-data analysis.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

## Context and decision

Current IArtifactContentStore has unconditional write/read/delete. Rewrap can overwrite a concurrent replacement. Content hashes cannot detect ABA (A → B → A). Establish a separate IVersionedArtifactContentStore : IArtifactContentStore capability in Core. Rewrap and recovery depend on it directly; composition fails when unsupported. No runtime downgrade to unconditional mutation.

Illustrative operations:

```csharp
Task<ArtifactContentSnapshot?> ReadVersionedAsync(ArtifactId id, CancellationToken ct);
Task<ArtifactContentMutationResult> CreateIfAbsentAsync(ArtifactId id, ReadOnlyMemory<byte> bytes, MutationContext operation, CancellationToken ct);
Task<ArtifactContentMutationResult> ReplaceIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, ReadOnlyMemory<byte> bytes, MutationContext operation, CancellationToken ct);
Task<ArtifactContentMutationResult> DeleteIfRevisionMatchesAsync(ArtifactId id, ArtifactContentRevision expected, MutationContext operation, CancellationToken ct);
Task<MutationReceipt?> GetMutationOutcomeAsync(OperationId id, CancellationToken ct);
```

Snapshot couples exact returned bytes with their store-issued opaque Revision. Result distinguishes Created/Replaced/Deleted, AlreadyExists, VersionConflict, Missing and Unsupported. Missing is an absence result, not permission to recreate during replace. An unsupported provider is rejected before a protected operation starts. For owned cleanup, mutation context includes owner and the store verifies owner together with revision; a raw revision match alone does not authorize lifecycle cleanup.

Every successful create/replacement/delete advances generation, including identical-byte replacement and legacy WriteAsync/DeleteAsync while they exist. Deletion produces a retained tombstone revision; subsequent recreation receives a fresh revision. Deleting already-absent content is a no-op with explicit missing outcome. Replaying an already-committed OperationId is recognition of the same mutation, not a new mutation and does not advance revision.

All physical mutations use the same cross-process coordination protocol. Preflight checks are advisory. Expected revision check, object state change and receipt creation commit atomically. Revisions are never caller-computed content hashes. Receipts carry structured operation/Artifact identity, mutation kind, old/new revision, ownership where applicable, outcome and time; never payload or keys. Reusing OperationId with different mutation parameters is an integrity conflict. Provider-private request fingerprints can cover encrypted input for replay comparison without exposing content hashes as Revision or putting digests in ordinary logs.

## Filesystem provider normative mechanism

SQLite MUST remain an implementation detail of FileSystemArtifactContentStore in Persistence. Provider-neutral interfaces MUST expose only snapshots, opaque revisions, mutation contexts/results and receipts; they MUST NOT mention SQL, transactions, catalog rows, journal modes or SQLite types. Other providers implement the same guarantees using their own infrastructure.

The filesystem provider MUST use a private provider-owned SQLite catalog and immutable generation files. The catalog is neither evidence metadata persistence nor an audit ledger. Only a generation referenced by committed catalog state is current content. An unreferenced durable generation MUST NOT be discovered as current content by filename scanning.

### Stable coordination and supported environment

The retained Artifact catalog row is its stable logical coordination identity, including while tombstoned. Catalog database locking MUST coordinate all processes. The initial implementation MUST serialize catalog mutations using non-deferred SQLite write transactions. Locks MUST NOT be attached to replaceable payload inodes; an in-process mutex is insufficient.

Generation preparation occurs before the mutation transaction. To prevent GC from reclaiming a live producer's not-yet-referenced generation, the provider MUST also use one stable root-level generation-coordination lock file. Initial supported implementation uses an OS-released cross-process shared/exclusive file lock on a supported single-host Linux local filesystem: producers hold the shared lock from before temporary creation through mutation outcome resolution; GC holds the exclusive lock throughout its catalog recheck and reclamation. This file MUST NOT be renamed, replaced or unlinked during normal operation. All participants acquire this gate before catalog write/exclusive coordination, avoiding inverted lock order. The gate protects staging lifetime only; it MUST NOT substitute for the atomic catalog revision check. Process death releases OS-held coordination; no stale PID/age-based lock breaking is permitted.

The provider MUST require validated local filesystem advisory-lock, SQLite-lock, same-filesystem rename, file flush and directory-flush semantics. Arbitrary network filesystems, multiple-host roots and unvalidated mounts are unsupported. SQLite MUST use rollback journal mode DELETE and synchronous EXTRA (including the directory durability required for journal removal), with settings verified rather than silently downgraded. Generation contents MUST be durably flushed; generation-directory entry creation/rename MUST be durably flushed before catalog promotion. Catalog/root initialization MUST also durably establish directories and SQLite files using a supported provider platform implementation. Storage hardware must honor required flushes. A platform lacking these guarantees MUST fail capability admission, not advertise durable mutation receipts. Busy waits, lock acquisition, size limits and recovery budgets MUST be bounded.

### Create/replace ordering

1. Acquire the stable shared generation gate. Validate bounded canonical request and identifiers. An optional receipt/precondition lookup is advisory; it cannot authorize promotion.
2. Create a unique immutable-generation candidate using a private CreateNew temporary file, preserving restrictive permissions and path/symlink validation. Write the complete payload, enforce bounds and durably flush the file.
3. Rename into the immutable generation namespace on the same filesystem and durably flush the directory. Existing immutable generations MUST NOT be overwritten.
4. Only after candidate durability, BEGIN the catalog mutation transaction. Recheck OperationId idempotency first, then exact expected revision/state/ownership. Another mutation during preparation MUST produce a conflict, never stale promotion.
5. Atomically record the new generation reference, fresh opaque revision, owner as applicable and durable operation receipt/outcome. COMMIT is the sole logical promotion point. Readers follow only committed references.
6. Acknowledge only after commit is known durable. If acknowledgement is uncertain, reconcile the same OperationId before reporting a committed result. Release generation gate after outcome resolution; unresolved outcomes remain review/recovery work.

A duplicate or rejected request may leave a candidate unreferenced; deferred GC reclaims it. Old generations MUST NOT be deleted synchronously in the promotion transaction. Payload durability precedes catalog commit; catalog transaction atomically promotes the durable payload reference, revision and receipt rather than updating separate revision sidecars.

### Revision continuity and deletion

Every successful state-changing create, replacement and delete MUST advance revision, including identical-byte writes and legacy APIs. Delete MUST commit a tombstone/current-state row with fresh revision and receipt; it MUST NOT erase lineage or physically unlink payload during the mutation. Tombstones MUST retain the non-reused mutation namespace across recreation. Deleting already-absent content returns Missing and does not pretend a mutation occurred. CreateIfAbsent may recreate tombstoned content but MUST issue a fresh revision. No A → delete → recreate A sequence may reuse the original revision.

Catalog epoch/non-reused generation encoding is private; callers MUST treat revision as opaque. Backup restore MUST fence all callers and initialize fresh current revision namespace before mutations resume, without erasing historical receipts. Backup rollback that loses required receipts/outcomes MUST become RequiresReview. Tombstones/receipts MUST NOT be pruned while outstanding recovery/audit work depends on them. Approved retention requires explicit reconciliation barriers and preservation of non-reuse continuity.

### OperationId idempotency

Same OperationId and identical canonical mutation request MUST return the original durable receipt/outcome, even if the current object has since changed. It MUST NOT rerun the mutation, issue a new revision or reevaluate the old request as a new operation.

Same OperationId and different canonical mutation request MUST raise an integrity/idempotency conflict and fail closed/RequiresReview. Canonical request identity MUST cover Artifact identity, mutation kind, expected revision/absence semantics, ownership constraints and provider-safe payload identity where applicable. It MUST NOT depend on staging filename, attempt number or newly generated time. A bounded provider-private digest is permitted for request comparison, but not as the opaque revision or ordinary log/audit fact. Persist committed non-mutating outcomes such as VersionConflict/AlreadyExists/Missing so retries recognize the original outcome. Intentional reevaluation requires a new explicitly coordinated logical mutation identity.

For encryption decorators, randomized encryption must not turn a retry of one logical request into different physical request bytes. The coordinator MUST preserve the exact encrypted mutation candidate/identity across retries in protected staging, or use an explicitly specified equivalent logical-to-physical idempotency binding. Plaintext and DEKs MUST NOT be persisted in operation intents. Retrying with different encrypted bytes under an unchanged OperationId without that binding MUST conflict.

### Deferred GC and reader safety

ReadVersionedAsync MUST hold a catalog read transaction while opening and fully copying its selected immutable generation and revision. It MUST return matching bytes/revision and release the transaction only after the copy is complete. Readers MUST NOT resolve a filename, release coordination and later open it unprotected.

GC is a distinct bounded recoverable operation. It MUST acquire the exclusive generation gate, then SQLite EXCLUSIVE coordination, wait for active readers to finish and block new catalog readers. It MUST recheck current references and protected staging/recovery dependencies before unlinking only eligible generations. It MUST NOT reclaim a live producer candidate because producers retain the shared gate until promotion/reconciliation. Unknown ownership or damaged catalog state MUST stop destructive reclamation. A physical receipt can remain after payload reclamation; event reconstruction MUST depend on bounded receipt data, not obsolete content.

GC MUST durably flush affected directories and release coordination after each bounded batch. Crash/retry of physical reclamation MUST tolerate already-missing eligible files; catalog current state remains authoritative. Age may govern scheduling but MUST NOT prove ownership or eligibility. No synchronous prior-generation deletion is part of promotion.

### Explicit crash windows

| Window | Required durable/recovery behavior |
| --- | --- |
| Writing temporary candidate | Prior catalog state remains current; partial/unreferenced candidate is never readable as current; after OS releases producer gate, coordinated GC may reclaim it |
| File flushed, rename/directory durability incomplete | Prior catalog state remains current; no mutation commit permitted; recover candidate as staging garbage |
| Generation durable, before BEGIN/commit | Candidate is an orphan generation, never current; GC checks catalog after live producer coordination ends |
| Catalog transaction active before commit | SQLite rollback/commit semantics determine the authoritative state; never infer success from candidate existence |
| Commit acknowledgement uncertain | Reconcile same OperationId receipt; unknown/damaged outcome requires review |
| Commit durable, response lost | Identical retry returns existing receipt/outcome; no additional mutation |
| After coordination released | Committed catalog reference/revision/receipt remain authoritative; only deferred GC reclaims obsolete generations |
| Delete transaction interrupted | Either prior present state or committed tombstone/receipt; no synchronous payload deletion can destroy the prior state |
| GC interrupted | Logical catalog state unchanged; eligible leftover files are reclaimed idempotently on retry; never delete referenced/live generations |
| Process dies holding any lock | OS/SQLite recovery releases or recovers coordination; stable identities persist; never unlink coordination objects to force progress |
| Catalog/current generation missing or corrupt | Fail closed/RequiresReview; never reinterpret root as empty or overwrite surviving content |

Process-kill tests do not prove power-loss durability. Deployment validation MUST cover documented filesystem/SQLite assumptions, backup consistency and damaged-state handling.

## Bootstrap and migration

Stop all old writers and readers. Under exclusive migration coordination inventory legacy validated Artifact filenames, import unchanged bytes into durable immutable generations, assign fresh opaque revisions and mark legacy ownership as unknown. Atomically establish catalog entries and resumable migration checkpoints; leave old files until migration verifies readback and no old binaries remain. Unknown legacy ownership is never eligible for automatic ingestion compensation. Missing catalog in an already initialized root is damage, not a fresh bootstrap signal.

Every legacy WriteAsync/DeleteAsync MUST route internally through the same generation gate, durable-before-catalog ordering, catalog state transaction, revision advancement and operation-outcome protocol. They MUST NOT access an old Artifact filename directly. Legacy signatures may generate a private per-invocation OperationId but cannot promise caller-level retry idempotency; security-sensitive callers MUST migrate to explicit operation identity and conditional capabilities. Legacy callers are then migrated to create/conditional replace/delete. Security-sensitive callers may not retain unconditional use. Versioned encryption decorator passes physical revisions unchanged and does not fabricate revisions from plaintext. No store root may be opened by pre-protocol binaries.

## Complete current writer migration inventory

- src/EMF.Security/Storage/ArtifactEnvelopeRewrappingService.cs:253 — unconditional promotion must become exact-revision replacement.
- src/EMF.Security/Storage/EncryptedArtifactContentStore.cs:55,74 — decorator must preserve all revision, operation receipt and ownership semantics; plaintext is encrypted before physical creation/replacement.
- src/EMF.Persistence/Storage/FileSystemArtifactContentStore.cs:35,125 — every legacy write/delete must use the catalog protocol.
- src/EMF.Orchestration/Services/EvidenceFileIngestionService.cs:109,178,195 — missing-content repair, new creation and compensation.
- src/EMF.Orchestration/Services/InventoryOrchestrationService.cs:137 — missing-content creation.
- src/EMF.Orchestration/Services/InventoryWorkflowActivity.cs:96,116,137 — creation/repair and cleanup.
- src/EMF.Orchestration/Services/EmailAttachmentExtractionService.cs:156,175 — creation and cleanup.
- src/EMF.Orchestration/Services/EmailMessageWorkflowActivity.cs:180,197 — creation and cleanup.
- src/EMF.Orchestration/Services/ZipEntryExtractionService.cs:153,172 — creation and cleanup.
- src/EMF.Extensions.VeteransClaims.Orchestration/VeteransBoundedEvidenceDerivationService.cs:255,272 — creation and cleanup.
- src/EMF.Extensions.VeteransClaims.Orchestration/VeteransClinicalNoteDerivationService.cs:112,219,234 — repair, creation and cleanup.

These are all current artifact-content mutation call sites found by searching src. Decoder stream writes and final document exports are not physical Artifact-content mutations. Composition in src/EMF.Console/ArtifactContentStoreFactory.cs must expose the versioned capability directly. No deployment may mix pre-protocol binaries with new writers against the same root.

## Verification, ownership and tradeoffs

Core owns neutral capability/models; Persistence owns catalog, generations and migrations; Security owns encryption decoration. Test multiple independent processes, ABA, create races, conditional delete, replay/different-request conflict, process death before/after commit, read/GC races, restore revision invalidation and legacy API participation. Preserved stale-rewrap regression must pass.

This introduces a catalog dependency and initially serializes writes across a root. Rejected: payload-file locks (rename changes identity), in-process-only locking, separate non-atomic revision sidecars, and hash-only preconditions. Alternative stable lock-file + atomic manifest remains possible only as a separately reviewed design with durable historical receipts; it is not an implementation fallback. References: ADR-016/021/022, ADR-048/049.

## ADR-049 audit-obligation reconciliation dependency

For a security-sensitive mutation, its canonical mutation context MUST bind the required outcome AuditEventId before commit. The durable receipt MUST atomically retain a bounded reconciliation tuple: OperationId, required AuditEventId, resource identity, mutation outcome/revision, deterministic occurrence time and audit-obligation schema/version. These are coordination identities/state, not audit event content or a second ledger. Missing required identity prevents protected promotion. Receipts MUST be enumerable by a bounded durable reconciliation interface, including when the corresponding operation journal row is absent. Receipt retention MUST NOT depend solely on journal-row existence; pending/review obligations cannot be pruned. ADR-049 defines canonical-chain reconciliation and guarded completion/retention. SQLite remains private to this filesystem provider; neutral contracts expose bounded receipt/obligation enumeration without SQL types. This clarification changes no generation, locking, revision or GC architecture.

## Related architecture decisions

- [ADR-016](ADR-016-provider-owned-versioned-schema-migrations.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-021](ADR-021-artifact-content-protection-boundary.md)
- [ADR-022](ADR-022-artifact-envelope-key-rewrapping-lifecycle.md)
- [ADR-048](ADR-048-artifact-ingestion-ownership-and-recovery.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)

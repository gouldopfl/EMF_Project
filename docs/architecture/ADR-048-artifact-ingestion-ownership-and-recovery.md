# ADR-048: Artifact Ingestion Ownership and Recovery

## Status

Accepted

## Date

2026-10-01

## Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

## Context and decision

Evidence ingestion writes protected bytes before metadata; cancellation skips current compensation, process death has no intent, and blind delete cannot prove exclusive ownership. Use durable intent before content creation, ADR-047 owned create-if-absent and exact-revision deletion, plus transactional metadata adoption. No distributed SQLite/filesystem transaction is required.

Intent schema contains only bounded non-sensitive coordination data: stable OperationId, provisional ArtifactId, opaque OwnershipToken, provisionally authoritative protection classification and ClassificationRevision, original authorized operation identity, physical Receipt/Revision, intent state/revision and timestamps. The provisional classification binding is durable in Prepared before physical creation; it is not a caller assertion. Original actor and recovery actor are bounded identities in the associated authorized security-operation context, not source paths or arbitrary intent facts. Evidence/provenance data remains in its existing governed metadata boundary. OwnershipToken is not a secret or substitute for authorization.

## Provisional classification authority (normative)

Before any physical content creation, the platform classification authority MUST establish an authorized provisional classification binding. Prepared MUST durably bind provisional ArtifactId, OwnershipToken, protection classification, a non-reused authoritative provisional ClassificationRevision, and original authorized operation identity. The platform resolves classification through governed ingestion policy/authority and checks the authenticated operation; arbitrary caller-supplied classification MUST NOT create this authority. Unknown/unresolvable classification prevents creation. Classification values and revision/operation identities use bounded approved schemas, never evidence text or paths.

The authoritative resolver MUST distinguish Provisional and Adopted authority while preserving resource type Artifact and the exact provisional ArtifactId for pre-adoption authorization. The ownership binding prevents using another intent's classification for cleanup. Classification revision is distinct from intent revision, ownership token and physical revision.

Before MetadataCommitted, recovery MUST resolve and verify this provisional authority, authorize cleanup under the recovery service identity against that classification and resource, and fence both ClassificationRevision and intent state/revision through CleanupClaimed. Physical deletion additionally checks OwnershipToken and exact physical Revision. A changed classification requires reauthorization; missing/corrupt authority, ownership ambiguity or an invalid original-operation binding becomes RequiresReview, never deletion. Original actor is preserved separately, not impersonated by recovery.

For ProvisionalArtifactAdopted, the metadata transaction MUST reconcile provisional authority with any proposed Artifact classification, promote the classification into ordinary Artifact authority, record the linkage from provisional revision to the new authoritative revision, and commit metadata plus MetadataCommitted together. A mismatch or ambiguous mapping MUST fail closed/RequiresReview. Do not select whichever classification is more permissive. After that commit, ordinary authoritative Artifact classification governs; provisional authority is retained only as governed historical/recovery evidence and cannot authorize ingestion compensation of adopted content.

For DeduplicatedToCanonicalArtifact:

- Resolve canonical Artifact classification independently from provisional authority. Deduplication MUST NOT overwrite canonical classification, copy provisional classification onto canonical content, or pretend the provisional object was adopted.
- Classification agreement must be established under the defined classification domain/derivation policy with both revisions fenced. Different labels/revisions are not assumed ordered or interchangeable. Mismatch, unsupported equivalence or ambiguity becomes RequiresReview for the deduplication result; do not choose the more permissive label or authorize canonical access from provisional authority.
- Cleanup of the unused provisional object, if separately permitted, remains authorized solely against its own valid provisional classification, ownership and exact physical revision. Canonical classification MUST NOT substitute for that cleanup authority. A canonical mismatch does not automatically prohibit independently authorized removal of the proven-owned provisional object, but the mismatch/review record MUST remain durable; cleanup never resolves the mismatch by itself.
- Canonical content and classification are never changed by provisional cleanup. Canonical identity is returned as an adopted result only when classification/integrity checks succeed. A canonical repair is a separately authorized operation governed by canonical authority.
- The transaction MUST record canonical identity, classification reconciliation outcome and independently authorized provisional cleanup state. If classification/cleanup cannot be established, keep RequiresReview; no lookup-then-delete shortcut.

Tests MUST cover cancellation before metadata adoption with valid provisional authority, arbitrary caller classification rejection, missing/corrupt provisional binding, reclassification racing cleanup, adoption mismatch, canonical dedup classification agreement/mismatch and separately authorized cleanup without touching canonical content. Existing classification resolver/intent contracts and persistence composition require this capability before new ingestion is enabled.

## States and transitions

Prepared → ContentCreated → MetadataCommitted → Completed.
ContentCreated → CleanupClaimed → Cleaned.
Ambiguous ownership, adoption or mutation outcome → RequiresReview.

Prepared exists before any physical write. If no physical creation occurred, a fenced Prepared intent can terminate as Cleaned (no physical deletion). If receipt proves creation despite a crash before ContentCreated, reconcile into ContentCreated before further action. Once MetadataCommitted, content is never eligible for ingestion compensation; Completed represents finished required lifecycle bookkeeping/audit delivery.

Metadata adoption returns ProvisionalArtifactAdopted, DeduplicatedToCanonicalArtifact, or Conflict/Failure, with canonical ArtifactId where applicable. ProvisionalArtifactAdopted is returned only for matching expected intent state/revision and ownership. Artifact/provenance/relationship commit, reconciled authoritative classification adoption and intent transition to MetadataCommitted MUST share the same metadata transaction. A classification-authority adapter unable to participate in that atomic adoption boundary is unsupported for this lifecycle; no temporary permissive classification or unfenced cross-store promotion is allowed. Store existing source/fingerprint dedup semantics without claiming a provisional ID was inserted when it was not.

For DeduplicatedToCanonicalArtifact, resolve and fence canonical classification as described below, commit the canonical adoption result, and transition the unused provisional intent to CleanupClaimed in the same metadata transaction only after cleanup authorization against its provisional authority; do not mark provisional content MetadataCommitted. If authorization is unavailable, retain non-destructive pending/review state rather than claiming cleanup is authorized. Return the canonical identity only after required consistency checks. Validate canonical metadata/content using existing integrity semantics; repair, when needed, is a separately authorized owned create protocol, not overwriting canonical content.

## Fencing and safe cleanup

Cleanup claim compares expected intent revision/state and ownership in a metadata transaction. That same coordinator excludes adoption; every participating metadata writer must use it. After claim, physical cleanup checks exact created Revision and OwnershipToken through ADR-047. Concurrent replacement or ownership change prevents deletion and requires reconciliation/review. Metadata lookup followed by unconditional deletion is prohibited. Age and GUID coincidence are never ownership evidence.

No legitimate adopter can commit through CleanupClaimed. Provisional content cannot be rewrapped/replaced as an ordinary committed object before adoption; participating mutation coordinators enforce the lifecycle state. If another writer bypasses this rule, ownership is ambiguous and recovery fails closed. Intent fencing and physical revision are separate safeguards, not a second physical locking system.

## Irreversible adoption persistence invariant (normative)

Once metadata adoption commits, Persistence MUST durably and monotonically record that the provisional Artifact was adopted. The immutable adoption marker MUST commit in the same metadata transaction as Artifact/provenance adoption, authoritative classification adoption and MetadataCommitted. It MUST remain independent of mutable intent, recovery and audit-delivery status. Persistence MUST reject subsequent mutation, clearing or deletion of this marker; deleting or losing an intent row MUST NOT delete adoption evidence. No transition, repair, cancellation, recovery action, missing intent row or RequiresReview state may make adopted content eligible for ingestion compensation again.

A cleanup claim MUST atomically verify all of the following within the metadata coordination boundary that excludes adoption:

- The lifecycle state is cleanup-eligible.
- No immutable adoption marker exists for the provisional Artifact.
- No committed adoption evidence exists for the provisional Artifact.
- The expected intent revision and state still match.
- Provisional ownership still matches.

MetadataCommitted, Completed, immutable adoption evidence, contradictory state or unresolved lifecycle damage MUST deny cleanup. Ambiguous or damaged state MUST become durable RequiresReview work, never cleanup permission. Absence of an intent or marker alone is not proof of non-adoption. Before executing or replaying physical cleanup, recovery MUST revalidate the persisted claim and adoption evidence; a stale or contradictory CleanupClaimed state cannot authorize deletion. Physical deletion additionally requires the classification/authorization fences and exact OwnershipToken and Revision checks specified above.

Recovery MUST honor irreversible adoption evidence even when mutable intent state incorrectly says ContentCreated or CleanupClaimed. It MUST reconcile or record RequiresReview without deleting the adopted content. RequiresReview answers whether automation may safely continue; it does not change ownership or adoption truth. Audit-delivery failures after adoption MUST leave committed adoption intact and retain pending delivery/reconciliation work, rather than report the adoption as uncommitted.

DeduplicatedToCanonicalArtifact does not mean the provisional Artifact was adopted. Its durable canonical-result linkage MUST distinguish that disposition from provisional adoption so independently authorized cleanup can remove only the unused provisional object. Canonical adoption evidence and content MUST remain untouched.

## Cancellation and crash recovery

Caller cancellation after content creation cannot erase the durable intent or disable recovery. Immediate compensation uses its own bounded service token and cleanup budget. Propagate caller cancellation with safe recovery-pending information when appropriate; do not mask it with raw cleanup/provider exceptions. Cleanup failure retains claim/recovery work, uses sanitized categories and bounded retries, and is observable. Do not hold plaintext while recovering.

| Crash point | Deterministic action |
| --- | --- |
| Before intent commit | No content creation allowed |
| Prepared, before content mutation | Receipt says no mutation: clean intent; otherwise reconcile receipt |
| Creation committed, intent not updated | Recover same OperationId receipt, advance ContentCreated |
| ContentCreated, metadata not committed | Fenced adoption or owned cleanup according to recorded outcome/policy |
| Metadata transaction in progress | Transaction rollback or committed metadata+intent result determines outcome |
| MetadataCommitted, before Completed | Complete bookkeeping/audit; never delete adopted content |
| CleanupClaimed, before delete | Repeat same conditional delete OperationId |
| Delete committed, before Cleaned | Receipt confirms deletion; finish intent |
| Missing receipt, divergent ownership or damaged state | RequiresReview; never speculative deletion |

Recovery scans persisted states, acquires bounded fenced claims, uses stable logical operation IDs for retries, checks receipts and metadata transaction outcomes, and completes idempotently. It never re-ingests original source paths or decrypts physical ciphertext to determine ownership. Its own failure preserves recoverable state, escalates after approved limits, and never abandons work as success.

## Authorization, audit and layers

Recovery service authenticates independently and obtains resource-neutral recovery authorization for the actual provisional Artifact using the durable provisional authority before MetadataCommitted, and ordinary authoritative Artifact classification afterward. Journal original actor separately from executing recovery service. Audit cleanup/reconciliation decisions and outcomes under ADR-049; failures follow ADR-020. When classification/ownership cannot be resolved, allow only separately authorized non-destructive review, not deletion.

Core owns neutral intent/adoption contracts and their classification/operation identity references; Security-owned concrete classification and authorization models are mapped at the boundary; Orchestration coordinates ingestion without cryptography; Persistence owns intents colocated with metadata and atomic adoption; Security enforces authorization and audit; physical ownership/revision/receipts use ADR-047.

## Affected files/callers and migration

- src/EMF.Core/Contracts/IEvidenceRepository.cs:48,53 — explicit adoption result/coordinated commit capability.
- src/EMF.Persistence/Repositories/SqliteEvidenceRepository.cs:451,489,528,624 — dedup result and same-transaction adoption/intent transition.
- src/EMF.Orchestration/Services/EvidenceFileIngestionService.cs:143,178,185,190,195 — prepared intent, owned creation, adoption, safe cancellation/cleanup.
- src/EMF.Orchestration/Contracts/IArtifactIdGenerator.cs and Services/GuidArtifactIdGenerator.cs — uniqueness helps identity but is not ownership proof.
- All create/repair/cleanup callers enumerated in ADR-047, including inventory, email/ZIP extraction and Veterans derivation, adopt this shared lifecycle rather than custom catch/delete logic.

Deploy schema/capability changes before enabling new workflows, stop incompatible writers, and inventory legacy objects without deleting unknown owners. Deployment configures recovery identity, bounded budgets, scans, retention and review escalation. Existing workflow recovery is not silently assumed to be an artifact orphan mechanism.

## Verification and alternatives

Preserved ingestion-cancellation regression must pass. Add focused cases for metadata exception, successful dedup to canonical ID, adoption/cleanup race, conditional replacement race, cleanup cancellation/failure, crash at every table transition and repeat recovery. Verify no deletion of canonical/adopted content.

Milestone 5 MUST additionally prove the irreversible adoption persistence contract with these regressions:

- A stale cleanup worker cannot acquire or execute a cleanup claim after adoption commits.
- A crash immediately after adoption commit recovers into bookkeeping/audit completion without deletion.
- Audit delivery failure leaves adoption intact and its delivery obligation pending.
- A missing or corrupt intent alongside committed adoption evidence becomes review/reconciliation work without deletion.
- Repeated recovery never returns adopted content to ContentCreated or CleanupClaimed or otherwise restores compensation eligibility.
- Direct attempts to mutate, clear or delete the immutable adoption marker after commit are rejected by persistence.
- When mutable intent says ContentCreated or CleanupClaimed but immutable adoption evidence exists, recovery honors the adoption evidence, records review/reconciliation as necessary and never deletes content.

Rejected compensation-only (no crash recovery), owner-only (no transactional adoption), age-based orphan scans and lookup-then-delete. Staging-before-metadata alone creates committed-but-unavailable content; durable intent plus fenced owned creation is preferred. References: ADR-017/021/031/033, ADR-047/049.

## ADR-047 protocol dependency (normative clarification)

All physical mutations MUST use ADR-047's exact-revision and OperationId protocol, including legacy APIs during migration. Generations become durable before the catalog mutation transaction; only committed references are current. Deletion preserves tombstone revision continuity and reclamation is deferred. Recovery MUST recognize the original receipt on identical retry, fail closed on changed request identity, and preserve exact encrypted candidates or the specified logical-to-physical idempotency binding. Physical SQLite catalog/lock/journal details MUST NOT enter provider-neutral contracts or these lifecycle APIs. Receipt retention must outlast dependent intent and audit-delivery reconciliation.

## Related architecture decisions

- [ADR-017](ADR-017-protected-regulated-information-boundary.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-021](ADR-021-artifact-content-protection-boundary.md)
- [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-033](ADR-033-workflow-operation-idempotency.md)
- [ADR-047](ADR-047-versioned-artifact-content-mutation.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)

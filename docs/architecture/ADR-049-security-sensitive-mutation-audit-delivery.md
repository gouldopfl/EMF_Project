# ADR-049: Security-Sensitive Mutation Audit Delivery

## Status

Accepted

## Date

2026-10-01

## Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

## Context and decision

Security-sensitive mutation can commit before required audit persistence. Define durable authorized operation intent and CommittedAuditPending; never claim a confirmed mutation failed merely because audit acknowledgement failed. Use ADR-047 stable OperationId/Receipt for crash reconciliation and preserve ADR-028 hash chain as canonical audit truth. The operation journal is recovery state, NOT a second audit ledger.

## Required sequence

1. Resolve authoritative resource/classification and authorize; persist bounded authorized operation intent and allocate/bind the required outcome AuditEventId before mutation. Intent persistence failure prevents mutation.
2. Perform exact-revision conditional mutation with stable OperationId and required outcome AuditEventId through ADR-047; commit the bounded receipt reconciliation tuple atomically with the mutation. Persist only non-sensitive execution/reconciliation data; no plaintext or key bytes in journal. If a prepared encrypted replacement must be retained for retry, it belongs in protected physical staging governed by ADR-047, not serialized into an intent.
3. Receive/query durable mutation Receipt.
4. Durably record committed result plus required canonical event payload and CommittedAuditPending delivery state.
5. Append event to canonical ADR-028 chain using structured AuditEventId idempotency.
6. Mark audit delivery complete only after recognized successful acknowledgement.

Before promotion, failure/cancellation may leave no mutation and produces an appropriate audited outcome. After known commit, failure/cancellation preserves committed outcome and pending delivery; do not throw a generic error implying no mutation. Unknown commit acknowledgement is reconciled by OperationId receipt or becomes RequiresReview. Never blind rollback: later writers may have legitimate newer revisions.

Receipt supplies enough durable identity/outcome/time to reconstruct the event from pre-mutation intent even when process dies after mutation but before journal update. Completed mutation parameters and event occurrence time are stable across retries. Receipts cannot be pruned while any required audit obligation is unacknowledged or under review, even if its journal row is absent/corrupt. Without durable receipt support this mutation capability is unsupported, not best-effort.

## Structured event identity and integrity

AuditEventId is a first-class structured field, distinct from OperationId and delivery attempts. One operation may yield separate authorization/outcome/recovery event IDs. IDs and any OperationId/original actor/recovery actor identities required for idempotency are explicit canonical fields protected by the audit integrity representation, not hidden inside arbitrary Facts.

Append contract:

- Same AuditEventId and identical canonical event: recognize prior successful append and return its acknowledgement.
- Same AuditEventId with different canonical event: integrity conflict, RequiresReview; no replacement, second append or silent acceptance.
- New AuditEventId: atomically append canonical event and dedup identity in the serialized chain transaction.

Canonical event schema fixes field order, null/string encoding, UTC occurrence time and deterministic allowlisted fact ordering/encoding. Compare canonical event bytes (or digest plus exact representation when required) excluding chain-link/storage-generated append metadata. Duplicate retries do not produce new event times or differing attempt facts.

Use integrity representation version 2 defined by the companion ADR-028 addendum, explicitly covering AuditEventId, OperationId and actor roles plus existing audit fields. Preserve historical version 0/1 rows as they are; never rewrite old hashes or fabricate past event IDs. Update independent verifier to validate mixed recognized versions and the legitimate version transition. Unknown versions fail closed. Enforce AuditEventId uniqueness and event-equality comparison in the same transaction as append. The companion ADR-028 addendum and schema migration are prerequisites to implementation; historical ADR-028 text and existing v0/v1 records remain preserved; the accepted dated addendum defines the version-2 extension.

## Recovery and monitoring

Crash after mutation before journal update: query same ADR-047 OperationId Receipt; confirmed commit transitions CommittedAuditPending. Missing/contradictory receipt or damaged journal requires review; no speculative rollback or replay with new identity. Crash after audit append before journal acknowledgement: same AuditEventId recognizes prior append, then completes journal. Recovery itself requires authorized service identity; preserve original actor in outcome event and emit a distinct recovery event when security-relevant. Never rewrite original event actor to recovery actor.

Pending age/count, failed delivery attempts and RequiresReview are bounded operational health telemetry for ADR-032, obtained from journal state and checked against canonical append acknowledgement where available. They are not alternate audit events or claims that the chain contains an unacknowledged event. Missing chain reachability triggers out-of-band approved alert sink/health signal; do not require the failed chain as the sole route to report its own outage. Health signals contain only approved operation/event/resource identifiers, state, timestamps and safe failure category. Delivery failure remains observable.

Journal schema is access-controlled, bounded and crash-durable with explicit integrity/reconciliation controls. It is not represented as equivalent to the tamper-evident canonical chain. Loss/tampering of journal or receipts requires review and monitoring. Audit chain external anchoring/retention duties from ADR-028 remain.

## Journal loss invariant (normative)

A durable committed security-sensitive mutation/receipt whose required AuditEventId is neither acknowledged in the canonical audit chain nor represented by valid pending journal state MUST be detectable during reconciliation and become recovery work or RequiresReview. Deletion/corruption of a journal row MUST NOT erase that obligation.

Before promotion, the mutation request MUST bind required outcome AuditEventId and bounded resource/operation identities. ADR-047 MUST atomically retain those identities with the committed receipt, outcome revision, deterministic occurrence time and obligation schema/version. The receipt holds no event Facts, policy payload, plaintext or keys. It is a mutation outcome and obligation index, NOT a second audit ledger and NOT evidence that audit append succeeded.

Recovery MUST enumerate committed receipt obligations independently of journal enumeration. For each required AuditEventId it MUST query/verify canonical chain acknowledgement and compare a present journal entry against the receipt's identities/state. An unavailable/unverifiable chain is Unknown, not acknowledged. A valid matching pending journal is delivery work; a missing/corrupt/conflicting journal with no recognized canonical event MUST create bounded recovery work or RequiresReview and notify monitoring. If complete original canonical event bytes can be recovered from validated independent intent/context, recreate delivery work with the same AuditEventId/time. Otherwise retain RequiresReview; never fabricate actor/classification/facts or silently append a different event under that ID.

Canonical acknowledgement MUST match the receipt OperationId/resource/outcome/time and event equality binding where recoverable; existence of an arbitrary event with the same ID is insufficient. An acknowledged matching event permits restoring delivery-complete bookkeeping. Contradictory identity/outcome or changed canonical data is an integrity conflict. Original actor must come from validated original operation context; reconciliation/review events record the recovery actor separately and do not invent the missing original outcome event.

Unacknowledged/review receipts and obligation identities MUST remain retained. Completion/pruning MUST require verified canonical acknowledgement plus approved audit/receipt retention and external anchoring barriers; journal completion alone is insufficient. Completion flags are hints until reconciled with canonical acknowledgement, never independent audit truth. Receipt/catalog/journal backups must preserve reconciliation coverage. Loss or rollback of all receipt and journal copies cannot be detected by those stores alone; independent inventory/checkpoints/backup controls and ADR-028 external anchoring remain deployment obligations. This limitation does not excuse loss of one journal row while its receipt survives.

ADR-032 monitoring MUST include missing/corrupt journal obligations detected by receipt reconciliation, overdue pending work and RequiresReview. Signals are bounded operational health evidence, not a competing event ledger. Service-authorized reconciliation and recovery decisions are audited canonically when available; outage reporting uses approved out-of-band monitoring until delivery recovers.

Tests MUST delete or corrupt one pending journal row after committed mutation, verify independent receipt discovery and review/recovery work, reject mismatching canonical acknowledgement, preserve obligations during chain outage, prohibit journal-only receipt pruning and verify idempotent repair without fabricated facts.

## Layer ownership and affected files

- EMF.Security owns operation/audit-delivery coordination and structured safe outcome models.
- src/EMF.Security/Storage/ArtifactEnvelopeRewrappingService.cs:253,393 — first mutation caller; intent, receipt reconciliation and CommittedAuditPending.
- src/EMF.Security/Auditing/Models/SecurityAuditRecord.cs — structured AuditEventId/OperationId/actor fields and canonical event model.
- src/EMF.Security/Auditing/ISecurityAuditSink.cs — acknowledged idempotent append capability without unsafe fallback.
- src/EMF.Security.Persistence.Sqlite/Auditing/SqliteSecurityAuditSink.cs and SecurityAuditHashChainWriter.cs — atomic uniqueness, canonical equality and append.
- src/EMF.Security.Persistence.Sqlite/Auditing/SecurityAuditRecordHasher.cs and SqliteSecurityAuditIntegrityVerifier.cs — versioned integrity representation.
- src/EMF.Security.Persistence.Sqlite/SecurityAuditSqliteMigrations.cs — structured fields and idempotency uniqueness migration.
- src/EMF.Security/Monitoring/SecurityAuditMonitoringService.cs and SecurityAuditMonitoringContracts.cs — bounded pending-work health integration with ADR-032.
- ADR-047 physical mutation provider and ADR-048 metadata/intent persistence — durable operation receipts and original-actor linkage.

## Migration, verification and alternatives

Implement durable receipt capability first, then journal and canonical event schema/version migration, then new mutation coordination. Old audit records retain their actual protection history. Stop incompatible audit writers before enabling new event/version assumptions. Deployment approves backlog thresholds, retry limits, retention, alert sinks, service identities and review ownership.

Test mutation succeeded/audit failed; journal failed after mutation; crashes before/after each acknowledgement; identical replay and changed-data identity conflict; multiple concurrent append/recovery processes; structured identity tampering; legacy/version transition verification; lost receipts; and no rollback over newer mutation. Confirm one canonical outcome event after repeated delivery.

Transactional outbox is preferred when mutation state and outbox share a DB. Filesystem mutation requires receipt-backed journal coordination. Rejected in-memory retry-only, silent audit loss, generic fail-after-success, and blind compensating rollback. References: ADR-017/022/028/031/032/033 and ADR-047/048.

## ADR-047 protocol dependency (normative clarification)

All physical mutations MUST use ADR-047's exact-revision and OperationId protocol, including legacy APIs during migration. Generations become durable before the catalog mutation transaction; only committed references are current. Deletion preserves tombstone revision continuity and reclamation is deferred. Recovery MUST recognize the original receipt on identical retry, fail closed on changed request identity, and preserve exact encrypted candidates or the specified logical-to-physical idempotency binding. Physical SQLite catalog/lock/journal details MUST NOT enter provider-neutral contracts or these lifecycle APIs. Receipt retention must outlast dependent intent and audit-delivery reconciliation.

## Related architecture decisions

- [ADR-017](ADR-017-protected-regulated-information-boundary.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-022](ADR-022-artifact-envelope-key-rewrapping-lifecycle.md)
- [ADR-028](ADR-028-tamper-evident-security-audit-storage.md)
- [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-032](ADR-032-security-alert-detection-boundary.md)
- [ADR-033](ADR-033-workflow-operation-idempotency.md)
- [ADR-047](ADR-047-versioned-artifact-content-mutation.md)
- [ADR-048](ADR-048-artifact-ingestion-ownership-and-recovery.md)

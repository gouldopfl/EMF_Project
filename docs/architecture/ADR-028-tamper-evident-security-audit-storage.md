# ADR-028: Tamper-Evident Security Audit Storage

**Status:** Accepted
**Date:** 2026-08-16

## Context

EMF security operations emit structured `SecurityAuditRecord` values and can
persist them through the SQLite security audit provider. Ordinary SQLite rows
can be modified, deleted, reordered, or inserted by an actor who gains database
write access. Without an integrity mechanism, later review cannot distinguish
authorized records from altered storage.

Audit integrity must improve without rewriting legacy records as though they
had always been protected. Concurrent writers must not create divergent chains.

## Decision

The SQLite security audit provider uses a versioned SHA-256 hash chain.

Schema migration 2 adds:

- `IntegrityVersion`
- `PreviousRecordHash`
- `RecordHash`
- a unique partial index for non-null record hashes

Rows created before migration 2 remain version `0` with null hash fields.
They form an explicit legacy prefix and are never represented as protected.

New records use integrity version `1`. Each record hash covers, in order:

- integrity version
- previous record hash
- operation
- resource type
- resource identifier
- subject identifier
- policy decision
- destination
- outcome
- UTC occurrence time
- exact stored facts JSON

Each nullable or non-null field is encoded with a signed, big-endian byte length
followed by its UTF-8 bytes. This prevents delimiter and null-versus-empty
ambiguity.

The writer begins a non-deferred SQLite transaction before reading the prior
hash. This serializes chain append operations and prevents concurrent writers
from creating two valid successors to the same record.

The writer fails closed when the latest row has an unsupported integrity
version, a missing hash, or a legacy row appears after protected records.

An independent verifier reads records by ascending SQLite identifier,
recomputes each protected hash, and validates every previous-hash link.

## Security Properties

The verifier detects:

- modification of hashed record content
- deletion of a record followed by another protected record
- reordering of protected records
- broken or substituted previous-hash links
- unsupported integrity versions
- legacy rows inserted after the protected chain begins

The verifier reports protected and legacy record counts, the verified chain
head identifier and hash, the first invalid record identifier, and a failure
reason.

## Limitations

A local hash chain does not independently detect:

- deletion of the final record or an entire final suffix
- replacement or rollback of the complete database
- compromise occurring before records reach SQLite
- authorized-but-malicious events written with valid hashes
- loss of the database and every copy of its chain head

Production deployments must periodically preserve the latest chain head and
record count in an independent, access-controlled system. Centralized audit
collection, retention, alerting, administrative separation, and recovery remain
required.

SHA-256 provides tamper evidence, not source authentication. A future deployment
may add a keyed MAC, digital signature, external transparency service, or
immutable centralized ledger when its key custody and operational model are
approved.

## Consequences

### Positive

- new audit records are tamper-evident without changing the audit contract
- legacy records remain distinguishable from protected records
- chain creation and verification are deterministic and testable
- concurrent append operations are serialized
- integrity failures produce explicit verification results

### Negative

- each write requires a transaction, prior-hash read, and SHA-256 computation
- legacy records are not retroactively protected
- local verification cannot prove that the database is complete
- external anchoring and operational monitoring remain deployment obligations

## Alternatives Considered

- Per-record hashes without chaining were rejected because deletion and
  reordering would not be detected.
- Rehashing legacy rows was rejected because it would misrepresent their
  historical protection.
- SQLite-only triggers were rejected because portable SHA-256 support and
  canonical application-field encoding were not available.
- HMAC was deferred because production key custody and rotation are not yet
  approved.
- Centralized logging alone was rejected as the only control because local
  audit persistence must still fail closed and support offline verification.

## References

- ADR-016: Provider-Owned Versioned Schema Migrations
- ADR-017: Protected and Regulated Information Boundary
- `docs/SECURITY_AUDIT_OPERATIONS.md`
- `docs/INCIDENT_RESPONSE_AND_MONITORING.md`
- `docs/NIST_CONTROL_MAPPING.md`
- `docs/THREAT_MODEL.md`


## Accepted Addendum — 2026-10-01: Version 2 Canonical Audit Identity and Idempotent Append

**Status:** Accepted
**Date:** 2026-10-01

This dated addendum extends the accepted decision. Original text above is preserved as architectural history. Where explicitly clarified below, this accepted addendum governs the current architecture; unrelated original decisions remain in force.

### Cross-ADR identities and dependency direction

Core-facing contracts MUST use Core-owned provider-neutral identity/value representations for operation, receipt, ownership, classification reference/revision and audit-obligation identity where those values cross the Core boundary. EMF.Core MUST NOT reference EMF.Security or its concrete models. Security owns authorization, classification authority, canonical audit-event models and lifecycle policy, and maps those models to the neutral contract representations. AuditEventId, ClassificationRevision, OperationId, Receipt and OwnershipToken retain their distinct validated identity semantics across that mapping; shared spelling or representation does not make them interchangeable. SQLite catalog and Azure SDK types remain implementation details outside Core.

Physical Revision identifies a store-issued object generation; provisional OwnershipToken establishes lifecycle ownership; mutation OperationId identifies a logical mutation and Receipt records its durable outcome; ClassificationRevision identifies authoritative classification state; AuditEventId identifies one canonical audit event. These are separate typed concepts. None silently substitutes for another. Identifiers are bounded, validated, non-sensitive values and contain no plaintext, wrapped key bytes, credentials or DEK/KEK material. An ownership token is an opaque coordination identifier, not a bearer authorization credential; authorization is independently required.

No Azure SDK types enter EMF.Core or provider-neutral EMF.Security contracts. Physical stores remain cryptography-unaware. Provider failures follow ADR-020 typed sanitized model. Recovery is authorized under its service identity, preserves the original actor separately, and is audited through ADR-049. Deployment policy, classification governance, recovery schedules, historical-key retention and alert escalation remain deployment obligations.

### Scope and observed version

Current src/EMF.Security.Persistence.Sqlite/Auditing/SecurityAuditRecordHasher.cs declares CurrentVersion = 1. The next integrity representation version SHALL be 2. This is an integrity representation version, not an assigned database schema migration number. Schema migration numbering must be discovered at implementation time. No old record is rewritten and no accepted limitation of ADR-028 is weakened.

### Structured version-2 event schema

Every new v2 event MUST contain a validated bounded AuditEventId, nullable structured OperationId where applicable, OriginalActorId, nullable ServiceActorId and nullable RecoveryActorId where applicable, plus existing operation, resource type/id, SubjectId (actual executing actor), policy decision, destination, outcome, OccurredUtc and allowlisted Facts. SubjectId MUST equal the actual executing principal; recovery records preserve OriginalActorId and explicitly name the recovery principal. Ordinary original-operation outcome events MUST NOT change actors when subsequently delivered by recovery; delivery actor is bookkeeping, not a rewrite of the original event. A separately emitted recovery-action event identifies the recovery actor.

AuditEventId identifies one canonical event, not one append attempt. IDs and roles MUST be structured hashed fields, never hidden in arbitrary Facts. Required field/role constraints are validated before append. For system-originated events, OriginalActorId is the validated original service principal, not an invented human.

### Deterministic canonical event encoding

CanonicalEventV2 uses a fixed domain tag `EMF-SECURITY-AUDIT-EVENT-V2` followed by fields in this exact order: AuditEventId, OperationId, OriginalActorId, ServiceActorId, RecoveryActorId, operation, resource type, resource id, SubjectId, policy decision, destination, outcome, OccurredUtc, canonical Facts.

Each scalar string is strict UTF-8, prefixed by a signed 32-bit big-endian byte length. Null is -1; empty is 0. Reject invalid Unicode, lengths beyond approved bounds, invalid enum spellings, and duplicate fact keys. Use validated exact identifier strings: no culture-sensitive comparison, trimming or Unicode normalization at append/retry. Policy/outcome enum representations use fixed canonical names. Null remains distinct from empty.

OccurredUtc MUST be normalized to UTC and encoded as `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'` with invariant culture and seven fractional digits. For mutation outcomes, time is the committed receipt's recorded occurrence time; authorization and recovery events record their own actual occurrence once. Allocate/store the event time before first delivery and reuse it exactly. Retries MUST NOT replace occurrence time with retry/append time. A UTC value is a declared event time, not proof of clock accuracy; deployment time synchronization remains required.

Facts MUST use an operation-specific key/value allowlist with bounded count/key/value lengths. Sort keys lexicographically by their strict UTF-8 byte sequences, independent of locale/input dictionary order. Encode non-null map as signed 32-bit big-endian entry count followed by framed key/value pairs. Values may be null only if their allowlisted schema permits it; null/empty distinctions remain. An empty map is count 0; null map is rejected. No ad hoc JSON serializer formatting, arbitrary user facts, protected content or key material enter the canonical representation. This framing defines canonical Facts, replacing v1's exact stored Facts JSON rule only for v2. A JSON view may be stored for queries, but MUST round-trip to these exact canonical facts and MUST be verified against canonical fields.

### Chain hash versus event equality

Version-2 RecordHash is SHA-256 over framed invariant string `2`, framed PreviousRecordHash, then the length-framed CanonicalEventV2 bytes. Store hashes as uppercase hexadecimal SHA-256 values. PreviousRecordHash is null only for the first protected record following an allowed legacy prefix or empty database. Every other v2 record links the actual preceding protected record's hash, including a v1 predecessor.

Event equality compares exact CanonicalEventV2 bytes. Chain-link, RecordHash, database row ID, append/acknowledgement time and storage-generated metadata are excluded from event equality; they remain governed append metadata and the previous hash remains covered by RecordHash. A digest may accelerate comparison but MUST NOT be the sole substitute for exact canonical equality. Event representation version/domain is part of the canonical identity; do not claim equality across unapproved representation mappings.

### Atomic append and uniqueness

AuditEventId MUST be unique across structured v2 events. Same AuditEventId plus identical canonical event MUST recognize the original successful append and return stable acknowledgement referencing its stored row/hash; no new chain node or timestamp is created. Same AuditEventId plus different canonical event MUST be an integrity conflict/RequiresReview, never overwrite or silently accept it.

In the same non-deferred serialized append transaction, validate current chain state, check AuditEventId uniqueness/equality, and either acknowledge the verified existing event or append a new linked record plus unique identity. Concurrent retries cannot produce duplicate events or divergent chain successors. Existing acknowledgement requires valid stored integrity evidence, not an unverified ID lookup; a damaged/unverifiable chain fails closed. A unique constraint is necessary but alone does not implement equality recognition.

### Historical preservation and verifier rules

Existing v0 legacy-prefix and v1 records MUST remain unchanged, including exact Facts JSON, timestamps and hashes. New structured identity columns are null on historical rows; no fabricated historical AuditEventId/OperationId/actors. A unique partial index over non-null structured event IDs preserves historical absence.

Verifier accepts only a legacy v0 prefix followed by valid v1 then v2 protected records, or valid v2 directly after an empty/v0-prefix store. Missing v0/v1 sections are allowed; a v0 row after protected records, or v1 after the v2 transition, is invalid. V1 uses its original exact representation and links; v2 uses this addendum's canonical fields and links. First v2 links the last v1 hash when present. Unsupported/unknown versions, required-field absence, duplicate v2 AuditEventId, malformed canonical data, broken links or hash mismatches fail closed. New writers MUST NOT emit v1 after v2 activation; incompatible writers are stopped before cutover.

Maintain ADR-028 detection of modification, intervening deletion, ordering/substitution and divergent chain creation. Local hashes still cannot independently prove suffix/completed-database retention or source authentication. External anchoring, backup/retention controls, administrative separation and ADR-032 monitoring remain required. This addendum grants no stronger historical protection to v0/v1.

### Affected files, migration and verification

- src/EMF.Security/Auditing/Models/SecurityAuditRecord.cs — explicit event/operation/actor identities and canonical schema validation.
- src/EMF.Security/Auditing/ISecurityAuditSink.cs — acknowledged idempotent append capability.
- src/EMF.Security.Persistence.Sqlite/Auditing/SecurityAuditRecordHasher.cs — version dispatch and v2 deterministic encoding without modifying v1.
- src/EMF.Security.Persistence.Sqlite/Auditing/SecurityAuditHashChainWriter.cs and SqliteSecurityAuditSink.cs — atomic uniqueness/equality/append.
- src/EMF.Security.Persistence.Sqlite/Auditing/SqliteSecurityAuditIntegrityVerifier.cs and SqliteSecurityAuditRecordReader.cs — structured v2 read/verification and valid mixed transition.
- src/EMF.Security.Persistence.Sqlite/SecurityAuditSqliteMigrations.cs — new structured columns/constraints; migration number discovered later.
- ADR-049 coordinator/monitoring — stable event time/identity, verified acknowledgement and journal-loss reconciliation.

Deployment must fence incompatible writers, migrate nullable historical columns without changing old records, validate mixed-chain verification and preserve external chain anchors. Use canonical byte test vectors for null/empty, UTF-8, facts order, timestamps and actor roles; concurrent identical/different retries; ID tampering; unknown versions; v0→v1→v2/v0→v2/empty→v2; reject reverse transitions and duplicate IDs. No live Azure validation is required. Neutral canonical models belong in Security; SQLite uniqueness/storage remains the persistence adapter's detail.

The canonical encoding is fixed by this accepted addendum. Schema migration number, deployment retention/anchoring and platform proof are implementation/deployment prerequisites, not permission to weaken these semantics. References: ADR-016/017/028/031/032 and ADR-049.

### Related architecture decisions

- [ADR-016](ADR-016-provider-owned-versioned-schema-migrations.md)
- [ADR-017](ADR-017-protected-regulated-information-boundary.md)
- [ADR-020](ADR-020-azure-key-management-adapter-boundary.md)
- [ADR-031](ADR-031-resource-neutral-authorization.md)
- [ADR-032](ADR-032-security-alert-detection-boundary.md)
- [ADR-049](ADR-049-security-sensitive-mutation-audit-delivery.md)

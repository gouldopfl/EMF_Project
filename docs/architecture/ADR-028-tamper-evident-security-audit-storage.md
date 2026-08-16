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

The verifier reports protected and legacy record counts, the first invalid
record identifier, and a failure reason.

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
- `docs/INCIDENT_RESPONSE_AND_MONITORING.md`
- `docs/NIST_CONTROL_MAPPING.md`
- `docs/THREAT_MODEL.md`

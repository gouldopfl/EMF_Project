# EMF Security Audit Integrity Operations

**Status:** Draft
**Date:** 2026-08-16

## Purpose

This procedure defines how an authorized operator verifies the tamper-evident
SQLite security audit chain and responds to a verification failure.

## Command

Run:

```bash
emf security audit verify <database-path>
```

The verifier opens the database in SQLite read-only mode and checks records in
ascending identifier order.

Exit codes:

| Code | Meaning |
|---|---|
| `0` | All legacy and protected records have valid structure and links |
| `1` | An integrity failure was detected |
| `2` | Usage, file access, or database execution prevented verification |

Successful output reports protected and legacy record counts, the latest
protected record identifier, and the chain-head SHA-256 hash. Preserve these
values in an approved independent system. Failure output reports the first
invalid record identifier and reason.

## Preconditions

The operator must:

- be authorized to read the audit database
- use a trusted EMF executable
- record the database path, host, operator, and UTC verification time
- avoid copying protected audit facts into unrestricted tickets or messages
- understand that legacy version-0 rows were not retroactively protected

## Verification Failure

Exit code `1` is a security event. The operator must:

1. stop avoidable writes to the affected database
2. preserve the original database and related journal or WAL files
3. record the command output and verification time
4. create a working copy for analysis
5. hash preserved evidence when technically feasible
6. follow `INCIDENT_RESPONSE_AND_MONITORING.md`
7. obtain authorization before repair, restoration, or return to service

Do not rewrite hashes, delete records, or rerun migrations as a repair action.

## Aggregate Operation Reports

`SqliteSecurityAuditOperationReporter` produces read-only counts by outcome
for a selected operation. It verifies the complete audit chain before querying
and refuses to report from a tampered database. Reports include the operation,
total count, outcome counts, first and last occurrence times, and chain head.
Detailed audit facts are not exposed by this aggregate report.

## Limitations

The local chain detects record modification, broken links, reordering, and
deletion when a later protected record remains. It cannot independently detect
deletion of the final record or final suffix, rollback of the complete database,
or replacement of the database and every local copy.

Production deployments must externally preserve chain heads and record counts,
centralize required audit records, schedule verification, and alert on failure.

## References

- `architecture/ADR-028-tamper-evident-security-audit-storage.md`
- `INCIDENT_RESPONSE_AND_MONITORING.md`
- `NIST_CONTROL_MAPPING.md`
- `THREAT_MODEL.md`

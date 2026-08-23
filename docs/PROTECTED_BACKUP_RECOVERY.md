# EMF Protected Backup and Recovery

## Status

Draft operational baseline — 2026-08-16

## Purpose

Define minimum controls for backing up and recovering EMF encrypted artifacts,
envelope metadata, databases, audit records, configuration, and cryptographic
key dependencies.

This document is an engineering procedure. It is not an authorization,
continuity-of-operations plan, or claim of regulatory compliance.

## Scope

The procedure applies to:

- encrypted artifact content and serialized envelopes
- inventory, workflow, extension, and security-audit databases
- provenance, correlation, and migration records
- approved configuration and infrastructure definitions
- Azure Key Vault key references and required historical key versions

Source plaintext, temporary files, credentials, tokens, and exported
data-encryption keys must not be added to backups.

## Mandatory Protection Controls

Production backup design shall:

- encrypt backup data in transit and at rest
- preserve encrypted content together with required metadata
- retain every key version needed to decrypt retained artifacts
- enable Key Vault soft delete and purge protection
- use least-privilege identities separated from normal application access
- restrict backup services through approved private networking
- prevent backup operators from independently purging keys and recovery points
- use immutable or deletion-protected recovery points where supported
- record backup, restore, deletion, and policy-change events
- alert on failures, disabled protection, destructive changes, and key loss
- define approved retention periods, recovery objectives, and legal holds
- test restoration without exposing protected production content

## Backup Procedure

1. Confirm the backup policy, retention rule, destination, and authorization.
2. Verify the backup identity and private network path.
3. Record the application version, schema versions, and backup correlation ID.
4. Quiesce writes or use a provider-supported consistent snapshot.
5. Back up encrypted artifact envelopes and their storage metadata.
6. Back up databases using provider-supported transactionally consistent tools.
7. Record every referenced Key Vault key name and version.
8. Verify that required historical key versions remain enabled and recoverable.
9. Complete the backup and capture provider job identifiers and timestamps.
10. Verify integrity, expected object counts, encryption, and retention state.
11. Record success or failure without logging protected content.

A collection of files copied while writes continue is not an approved database
backup unless the persistence provider documents snapshot consistency.

## Recovery Procedure

1. Declare the recovery event and assign an authorized recovery coordinator.
2. Identify the approved recovery point and affected security boundary.
3. Preserve incident evidence when compromise or destructive activity is
   suspected.
4. Restore into an isolated recovery environment with restricted access.
5. Restore configuration and database state before dependent artifact content.
6. Recover required Key Vault objects or historical versions when necessary.
7. Re-establish managed identities, RBAC, private endpoints, and monitoring.
8. Validate schema migrations before allowing application access.
9. Verify envelope structure, artifact binding, and representative decryption.
10. Verify database integrity, provenance links, audit continuity, and counts.
11. Perform malware and compromise checks before promotion.
12. Obtain approval before returning the environment to service.
13. Record outcomes, exceptions, data loss, elapsed recovery time, and actions.

## Recovery Acceptance Criteria

Recovery is successful only when:

- restored data matches the approved recovery point
- database integrity and migration ledgers validate
- sampled encrypted artifacts decrypt under their original artifact identities
- replaying an envelope under another artifact identity is rejected
- required historical key versions are available
- audit and provenance continuity are understood and documented
- restored identities, permissions, networking, and monitoring are approved
- no unresolved compromise indicator blocks return to service

## Exercises and Evidence

Before production use, the system owner shall approve:

- recovery-point objective and recovery-time objective
- backup frequency and retention schedule
- geographic and subscription separation
- recovery roles and separation of duties
- exercise frequency and acceptable recovery evidence

A recovery exercise is required after material changes to persistence,
encryption, key management, backup architecture, or recovery procedures.
Exercises must record the recovery point, participants, timing, validation
results, exceptions, corrective actions, and approval.

Synthetic or specifically authorized test data shall be used until EMF is
approved for production protected information.

## Azure Key Vault Considerations

Key Vault backup and restore operates on individual keys, secrets, and
certificates rather than an entire vault. Manual backup is not a substitute for
soft delete, purge protection, redundancy, least privilege, and tested recovery.

Key inventory must account for version limits, retention dependencies, rotation,
revocation, regional recovery, and the inability to decrypt retained envelopes
after a required historical key becomes permanently unavailable.

## Open Production Decisions

The following remain unresolved and block production readiness:

- authoritative data inventory and business-impact analysis
- approved RPO, RTO, retention, and legal-hold requirements
- selected Azure backup services, vault types, regions, and redundancy
- immutable-vault and multiuser-authorization configuration
- Key Vault recovery and emergency-access ownership
- centralized monitoring, alerting, and incident escalation
- completed recovery exercise with documented remediation

## References

- [NIST SP 800-34 Rev. 1](https://csrc.nist.gov/pubs/sp/800/34/r1/upd1/final)
- [Azure Key Vault backup and restore](https://learn.microsoft.com/azure/key-vault/general/backup)
- [Azure Key Vault reliability](https://learn.microsoft.com/azure/reliability/reliability-key-vault)
- [Azure Backup security](https://learn.microsoft.com/azure/backup/security-overview)


## Source Repository Backup

The EMF Git repository shall be protected independently of both the Azure VM
working copy and the primary GitHub remote.

Minimum protection:

- Azure VM working repository
- GitHub primary remote
- independent Git mirror backup
- second copy of that mirror on separate storage

Create the initial mirror with `git clone --mirror`.

Refresh an existing mirror with `git remote update --prune`.

Verify the mirror with:

- `git fsck --full`
- `git show-ref`

The verified mirror should then be copied or synchronized to independently
protected storage such as the NAS.

A successful GitHub push is source control, but it does not by itself satisfy
the independent-backup requirement.

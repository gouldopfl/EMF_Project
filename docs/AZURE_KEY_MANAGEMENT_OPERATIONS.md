# EMF Azure Key Management Operations

## Status

Draft operational baseline — 2026-08-16

## Purpose

Define production requirements for EMF key-encryption keys hosted by Azure
Key Vault or Managed HSM.

This document does not claim that the controls are deployed or that EMF is
authorized for production protected information.

## Application Identity and RBAC

Production workloads shall use a dedicated managed identity and the Azure RBAC
permission model.

The application identity shall receive `Key Vault Crypto Service Encryption
User` at the narrowest practical scope. It requires key metadata plus wrap and
unwrap operations; it shall not create, delete, rotate, recover, purge, export,
or administer keys or role assignments.

Separate identities and approval paths shall be used for:

- application wrap and unwrap operations
- key lifecycle administration
- role assignment administration
- monitoring and security investigation
- recovery
- emergency access
- purge operations

Purge permission shall not be assigned to the application, routine key
administrators, or backup operators. Privileged access shall require MFA,
time-limited elevation, approval, justification, and audit.

## Network Isolation

Production vaults shall:

- use an approved private endpoint and private DNS
- disable public network access after private connectivity is verified
- restrict management paths to approved administrative networks
- separate development, test, and production vaults
- deny deployment when the private endpoint or DNS path is unhealthy
- monitor rejected connections and network-configuration changes

Application configuration shall use an approved HTTPS vault URI. URI validation
inside the adapter is defense in depth; it does not replace Azure networking,
Policy, DNS, or firewall enforcement.

## Key Creation and Rotation

Production key-encryption keys shall be non-exportable and created through
approved infrastructure or an authorized lifecycle process. The approved key
type, size, algorithm, activation time, expiration policy, owner, and purpose
shall be recorded.

Rotation shall:

1. Create and validate a new key version.
2. Update the configured current key version through an approved deployment.
3. Verify new-envelope encryption and decryption using synthetic content.
4. Rewrap retained artifact envelopes incrementally.
5. Record rewrapping progress, failures, and remaining dependencies.
6. Confirm that no retained envelope depends on a version proposed for
   retirement.
7. Disable an old version before deletion when policy permits.
8. Preserve recovery capability throughout the required retention period.

Routine rotation and emergency rotation are separate procedures. Rotation
shall never silently substitute the Development provider.

## Historical-Key Retention

Every serialized envelope records the key name and version needed to unwrap its
data-encryption key. A historical version shall remain enabled and recoverable
until one of these conditions is documented:

- every retained dependent envelope has been successfully rewrapped
- all dependent artifacts have reached approved disposition
- an authorized risk owner accepts permanent loss of decryption capability

Key dependency inventory shall be reconciled against artifact retention,
backup retention, legal holds, and disaster-recovery copies before retirement.
Backup copies containing old envelopes extend the required key-retention
period.

## Deletion Protection and Recovery

Production vaults shall use soft delete and purge protection. Retention settings
shall align with artifact, backup, legal-hold, and incident-response needs.

Deletion, disablement, recovery, and purge operations require authorization and
audit. Purge shall require separation of duties and shall not occur while any
retained envelope or recovery point depends on the affected key version.

## Monitoring and Alerting

Key Vault diagnostic and activity logs shall be sent to centralized,
access-controlled, retention-protected storage. Alerts shall cover:

- failed or unusual wrap and unwrap activity
- authentication and authorization failures
- role assignment and privileged-access changes
- firewall, private-endpoint, DNS, and public-access changes
- key creation, import, rotation, disablement, deletion, recovery, and purge
- keys nearing expiration
- soft-delete or purge-protection configuration changes
- sustained throttling, service errors, or unavailability
- failed envelope rewrapping and historical-key lookup

Alerts require an owner, severity, response target, escalation path, and
periodic test.

## Compromise and Emergency Procedure

When compromise, loss, or unavailability is suspected:

1. Open an incident and preserve relevant logs and configuration evidence.
2. Identify affected identities, vaults, keys, versions, envelopes, and backups.
3. Revoke or constrain compromised identity access without deleting keys.
4. Prevent unauthorized purge or lifecycle changes.
5. Establish a new approved key version when rotation is required.
6. Rewrap affected envelopes under controlled, auditable execution.
7. Validate representative decryption and artifact binding.
8. Recover deleted keys within the protection window when applicable.
9. Use emergency access only through an approved, time-limited procedure.
10. Remove emergency access, review actions, and record corrective measures.

Loss of a required historical key is a data-availability incident. The system
shall fail closed and shall not replace protected content with unreadable or
newly generated substitutes.

## Verification and Open Decisions

Before production approval, EMF requires:

- infrastructure-as-code enforcing vault, RBAC, network, and deletion controls
- approved rotation, retention, revocation, and purge schedules
- centralized diagnostics, alerts, and response ownership
- tested key recovery and emergency-access exercises
- verified inventory of envelope dependencies by key version
- authorized Azure integration tests using synthetic content
- documented remediation for every failed exercise

## References

- [Azure Key Vault RBAC](https://learn.microsoft.com/azure/key-vault/general/rbac-guide)
- [Azure Key Vault network security](https://learn.microsoft.com/azure/key-vault/general/network-security)
- [Secure Azure Key Vault keys](https://learn.microsoft.com/azure/key-vault/keys/secure-keys)
- [Azure Key Vault backup and restore](https://learn.microsoft.com/azure/key-vault/general/backup)

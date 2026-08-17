# EMF Government Security Readiness

## Status

Draft assessment — 2026-08-15

## Scope

Engineering readiness for government workloads. This is not a compliance certification or authorization.

## Current Determination

Strong security architecture, but not yet ready for production PII, PHI, or CUI. Development and testing should use synthetic or public data.

## Verified Strengths

- Fail-closed authorization and provider routing
- Managed identity with no API-key configuration
- Envelope encryption and key rewrapping
- Auditing, provenance, correlation, and timing
- 514 passing automated tests
- Pinned CI, enforced NuGet auditing, and CodeQL scanning
- Secret scanning, push protection, Dependabot, and an SPDX 2.3 SBOM
- Initial [NIST control evidence mapping](NIST_CONTROL_MAPPING.md) documented
- Initial [threat model](THREAT_MODEL.md) with residual-risk priorities
- Draft [protected backup and recovery](PROTECTED_BACKUP_RECOVERY.md) procedure
- Draft [Azure key-management operations](AZURE_KEY_MANAGEMENT_OPERATIONS.md) baseline
- CI-validated [Azure Key Vault Bicep profile](../infra/key-vault/README.md)
- CI-validated [Azure Policy baseline](../infra/policy/README.md) for Key Vault safeguards
- Read-only [Azure Policy compliance checker](../infra/policy/README.md) with
  fail-closed authorization handling
- Draft [incident-response and continuous-monitoring baseline](INCIDENT_RESPONSE_AND_MONITORING.md)
- Versioned tamper-evident SQLite audit chain with
  [ADR-028](architecture/ADR-028-tamper-evident-security-audit-storage.md) and
  independent verification
- Draft [security audit integrity operations](SECURITY_AUDIT_OPERATIONS.md)
  procedure
- Revision-based workflow optimistic concurrency with atomic transition history
  documented in [ADR-029](architecture/ADR-029-workflow-optimistic-concurrency.md)

## Principal Gaps

- Complete NIST SP 800-53 Moderate baseline mapping and tailoring
- Hardened production deployment
- Independent penetration testing and remediation
- Approve and exercise incident response; deploy centralized continuous monitoring
- Authorized Azure live integration test

## Target Standards

- NIST SP 800-53 Moderate
- NIST SP 800-171 Revision 3 when CUI applies
- NIST SP 800-218 secure development practices

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
- 469 passing automated tests
- Pinned CI, enforced NuGet auditing, and CodeQL scanning
- Secret scanning, push protection, Dependabot, and an SPDX 2.3 SBOM
- Initial [NIST control evidence mapping](NIST_CONTROL_MAPPING.md) documented
- Initial [threat model](THREAT_MODEL.md) with residual-risk priorities

## Principal Gaps

- Complete NIST SP 800-53 Moderate baseline mapping and tailoring
- Hardened production deployment
- Independent penetration testing and remediation
- Incident response and continuous monitoring
- Authorized Azure live integration test

## Target Standards

- NIST SP 800-53 Moderate
- NIST SP 800-171 Revision 3 when CUI applies
- NIST SP 800-218 secure development practices

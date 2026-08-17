# EMF NIST Security Control Mapping

## Status

Draft engineering evidence map — 2026-08-16

## Purpose

This document maps current EMF repository evidence to selected controls from:

- NIST SP 800-53 Revision 5, Release 5.2.0
- NIST SP 800-171 Revision 3
- NIST SP 800-218 SSDF Version 1.1

## Scope

This is an engineering traceability assessment. It is not a compliance
certification, security authorization, Authority to Operate, or assertion
that EMF satisfies a complete NIST baseline.

EMF is not approved for production PII, PHI, or CUI. Development and testing
must continue to use synthetic or public data.

## Mapping Status

- **Application-supported** — repository evidence supports the
  application-controlled portion of the control.
- **Partial** — evidence exists, but material requirements remain open.
- **External/unverified** — deployment or organizational evidence is missing.
- **Gap** — sufficient implementation or evidence does not exist.

An application-supported designation does not mean the complete NIST control
is implemented across a production system.

## NIST SP 800-53 Revision 5 Mapping

### AC-3 — Access Enforcement

**Status:** Application-supported

`AuthorizationPolicy` denies blank subjects, missing contexts, and absent
permissions. Authorization and composite-policy tests verify fail-closed
enforcement. Production identity integration remains unverified.

### AC-6 — Least Privilege

**Status:** Partial

Typed roles and permissions constrain application access. GitHub workflows
use read-only repository permissions unless an additional permission is
required. Production roles, access reviews, and separation of duties remain
undefined.

### AU-2 — Event Logging

**Status:** Partial

Security operations emit structured records through `ISecurityAuditSink`,
with SQLite persistence and automated tests. Organization-approved event
categories and production logging scope remain undefined.

### AU-3 — Content of Audit Records

**Status:** Application-supported

`SecurityAuditRecord` captures the operation, resource type and ID, subject
ID, policy decision, destination, outcome, UTC occurrence time, and structured
facts. Production privacy-minimization rules remain undefined.

### AU-5 — Response to Audit Logging Process Failures

**Status:** Partial

Executor tests verify audit-sink failure behavior and prevent silent success.
A versioned hash chain and independent verifier detect record modification and
broken chain links. A draft incident-response baseline defines evidence and
fail-closed response requirements. Scheduled verification, production alerting,
recovery, escalation, and ownership remain undefined.

### AU-12 — Audit Record Generation

**Status:** Application-supported

Audit contracts, executor integration, SQLite persistence, schema migrations,
and automated tests provide application-level audit generation. New records
use a versioned SHA-256 hash chain with an independent verifier. Centralized
production collection, external chain-head anchoring, and complete deployed-
component coverage remain open.

### CA-7 — Continuous Monitoring

**Status:** Partial

CI runs on pushes and pull requests. CodeQL runs on pushes, pull requests,
and a weekly schedule. A draft monitoring baseline defines sources, alert
conditions, evidence handling, and response activities. Production telemetry,
thresholds, ownership, exercises, and response approval remain undefined.

### CM-2 — Baseline Configuration

**Status:** Partial

`global.json`, centralized build settings, CI configuration, architecture
decisions, and the compiled and linted Azure Key Vault Bicep profile establish
repository baselines. Azure Policy assignments are compiled and linted in CI,
which also validates the compliance checker's shell syntax. Approved
environment parameters, deployment, monitoring-identity authorization, and
scheduled compliance evaluation remain open.

### CM-3 — Configuration Change Control

**Status:** Partial

Git history, pull-request CI triggers, pinned workflow actions, and
architecture decisions provide change traceability. Retained evidence of
required reviews, approval rules, and emergency procedures remains open.

### CM-6 — Configuration Settings

**Status:** Partial

Centralized build-security settings enforce NuGet auditing. Azure Key Vault
configuration validates vault URIs. Bicep defines RBAC, private networking,
private DNS, deletion protection, diagnostics, and deny-by-default access.
Azure Policy denies unsafe RBAC, public-access, deletion-protection, and
key-expiration settings and audits private-link compliance. A read-only checker
validates expected assignment identifiers, enforcement modes, effects, and
reported noncompliance. Approved deployment, monitoring identity, scheduled
execution, and remediation remain open.

### CP-9 — System Backup

**Status:** Partial

The [protected-backup procedure](PROTECTED_BACKUP_RECOVERY.md) defines
scope, encryption, consistency, historical-key dependencies, integrity
verification, separation of duties, and recovery evidence. Production services, retention, RPO, geographic
separation, and an exercised backup policy remain open.

### CP-10 — System Recovery and Reconstitution

**Status:** Partial

The recovery procedure requires isolated restoration, identity and network
re-establishment, schema and database validation, artifact-binding checks,
audit review, and approval before return to service. No production recovery
exercise has been completed.

### IA-5 — Authenticator Management

**Status:** Partial

Azure adapters use managed identity rather than application API-key
configuration. Azure identity lifecycle, MFA, workload identity controls,
credential policies, and access reviews remain externally unverified.

### RA-5 — Vulnerability Monitoring and Scanning

**Status:** Partial

CodeQL security-extended analysis, transitive NuGet vulnerability auditing,
Dependabot, secret scanning, and push protection are enabled. Infrastructure,
container, dynamic, and production scanning remain open.

### SA-10 — Developer Configuration Management

**Status:** Partial

Source control, ADRs, pinned CI actions, deterministic SDK selection, build
verification, and tests govern repository changes. Formal release, approval,
and configuration-accounting procedures remain undefined.

### SA-11 — Developer Testing and Evaluation

**Status:** Application-supported

The regression baseline is 505 passed, one intentionally skipped live test,
and zero failed. CI, CodeQL, and dependency auditing are automated. Threat
modeling, penetration testing, and authorized Azure live testing remain open.

### SA-15 — Development Process, Standards, and Tools

**Status:** Partial

Architecture decisions, centralized build settings, pinned automation,
static analysis, dependency auditing, and tests provide repeatable practices.
A formally approved secure-development policy and review cadence remain open.

### SC-12 — Cryptographic Key Establishment and Management

**Status:** Partial

Azure Key Vault references include explicit key versions. Envelope keys are
generated per operation, and key wrapping and rewrapping are tested. Draft
[Azure key-management operations](AZURE_KEY_MANAGEMENT_OPERATIONS.md) define
RBAC, isolation, rotation, retention, recovery, monitoring, and emergency
requirements. The Bicep profile defines a non-exportable wrap/unwrap key,
rotation policy, least-privilege workload role, purge protection, and soft
deletion. Production deployment and exercises remain open.

### SC-13 — Cryptographic Protection

**Status:** Partial

Envelope encryption uses AES-256-GCM with a random 256-bit data-encryption
key, 96-bit nonce, 128-bit authentication tag, and explicit key-memory
clearing. FIPS validation of the deployed modules remains unverified.

### SC-28 — Protection of Information at Rest

**Status:** Application-supported

`EncryptedArtifactContentStore` encrypts content before persistence,
cryptographically binds new envelopes to artifact identity, rejects
cross-artifact replay, and decrypts content when read. Azure Key Vault
protects envelope keys. Draft
[protected-backup and recovery procedures](PROTECTED_BACKUP_RECOVERY.md) are
documented. Production implementation, recovery testing, temporary data,
metadata, logs, and every storage path remain open.

### SI-2 — Flaw Remediation

**Status:** Partial

NuGet auditing, Dependabot, CodeQL, and build enforcement expose known
dependency and code weaknesses. Severity-based remediation deadlines,
exception handling, ownership, and closure evidence remain undefined.

### SI-7 — Software, Firmware, and Information Integrity

**Status:** Partial

GitHub actions are pinned by commit SHA. CI verifies restore, release build,
tests, and dependencies, and an SPDX 2.3 SBOM is available. SQLite audit
records use a versioned SHA-256 hash chain with independent verification.
Workflow execution revisions reject stale updates, while transactions bind
accepted status changes to transition history. External audit-chain anchoring,
workflow checkpoint and history integrity, signed releases, provenance
attestations, and deployment verification remain open.

### SR-3 — Supply Chain Controls and Processes

**Status:** Partial

Pinned automation, NuGet auditing, Dependabot, CodeQL, and SBOM generation
reduce software supply-chain risk. Supplier assessment, approved-source,
component provenance, and response procedures remain undefined.

## NIST SP 800-171 Revision 3 Relationships

These relationships apply only if EMF is deployed within a defined
nonfederal system boundary that processes, stores, or transmits CUI.

- **03.01.02 — Access Enforcement:** Application-supported through
  fail-closed authorization and permission tests.
- **03.03.01 — Event Logging:** Partial through structured security auditing
  and SQLite persistence.
- **03.03.02 — Audit Record Content:** Application-supported through the
  defined operation, resource, subject, decision, outcome, time, and facts.
- **03.03.03 — Audit Record Generation:** Application-supported through
  audit contracts, execution integration, persistence, migrations, and tests.
- **03.04.01 — Baseline Configuration:** Partial through the versioned SDK,
  centralized build settings, CI configuration, and ADRs.

- **03.04.02 — Configuration Settings:** Partial through validated Azure
  options and enforced NuGet audit settings.
- **03.11.02 — Vulnerability Monitoring and Scanning:** Partial through
  CodeQL, NuGet auditing, Dependabot, and repository security scanning.
- **03.13.10 — Cryptographic Key Establishment and Management:** Partial
  through versioned Key Vault references, wrapping, and rewrapping.
- **03.13.11 — Cryptographic Protection:** Partial through AES-256-GCM
  envelope encryption; deployed FIPS validation remains open.
- **03.14.01 — Flaw Remediation:** Partial because automated detection exists,
  but a formal remediation and evidence-retention process does not.

The organization-defined parameters required by SP 800-171 Revision 3 have
not been selected. No claim of CUI compliance is made.

## NIST SP 800-218 SSDF Mapping

- **PO.1 — Define Security Requirements:** Partial. Security ADRs and
  readiness documentation exist; an approved security policy does not.
- **PO.3 — Implement Supporting Toolchains:** Partial. Pinned CI, CodeQL,
  NuGet auditing, Dependabot, secret scanning, and push protection exist.
- **PO.5 — Define Security Check Criteria:** Partial. Automated build, test,
  dependency, and analysis gates exist; release criteria need formalization.
- **PS.1 — Protect Code from Unauthorized Access and Tampering:** Partial.
  Repository protections exist; administrative governance remains external.
- **PS.2 — Verify Software Release Integrity:** Gap. Signed releases and
  verifiable provenance attestations are not implemented.

- **PW.4 — Reuse Well-Secured Software:** Partial. Dependency restoration,
  vulnerability auditing, Dependabot, and the SBOM support governance.
- **PW.7 — Review or Analyze Human-Readable Code:** Partial. CodeQL analysis
  is automated; formal human security-review criteria remain open.
- **PW.8 — Test Executable Code:** Application-supported. The 505-test
  baseline and CI verify behavior and security boundaries.
- **PW.9 — Secure Settings by Default:** Partial. Invalid Azure settings are
  rejected and authorization fails closed; production hardening remains open.
- **RV.1 — Identify Vulnerabilities Continuously:** Partial. Scheduled CodeQL
  and continuous dependency monitoring are enabled.
- **RV.2 — Assess and Remediate Vulnerabilities:** Gap. Ownership, severity
  rules, deadlines, exceptions, and closure evidence are undefined.
- **RV.3 — Analyze Root Causes:** Gap. A repeatable vulnerability root-cause
  and recurrence-prevention process is not documented.

- **PW.4 — Reuse Well-Secured Software:** Partial. Dependency restoration,
  vulnerability auditing, Dependabot, and the SBOM support governance.
- **PW.7 — Review or Analyze Human-Readable Code:** Partial. CodeQL analysis
  is automated; formal human security-review criteria remain open.
- **PW.8 — Test Executable Code:** Application-supported. The 505-test
  baseline and CI verify behavior and security boundaries.
- **PW.9 — Secure Settings by Default:** Partial. Invalid Azure settings are
  rejected and authorization fails closed; production hardening remains open.
- **RV.1 — Identify Vulnerabilities Continuously:** Partial. Scheduled CodeQL
  and continuous dependency monitoring are enabled.
- **RV.2 — Assess and Remediate Vulnerabilities:** Gap. Ownership, severity
  rules, deadlines, exceptions, and closure evidence are undefined.
- **RV.3 — Analyze Root Causes:** Gap. A repeatable vulnerability root-cause
  and recurrence-prevention process is not documented.

## Principal Unverified or Missing Areas

- Complete SP 800-53 Moderate baseline selection and tailoring
- Organization-defined parameters and control ownership
- System security plan and production authorization boundary
- Hardened and versioned Azure production architecture
- Production identity governance, MFA, and privileged access
- Centralized log protection, retention, review, and alerting
- Incident-response plan approval, role assignment, exercises, and reporting
- Backup, continuity, disaster recovery, and recovery testing
- Continuous production monitoring and vulnerability management
- Threat modeling, abuse cases, and penetration testing
- FIPS-validated cryptographic deployment
- Signed builds, release provenance, and deployment verification
- Physical, personnel, media, privacy, and training controls
- Authorized Azure live integration testing
- CUI identification, marking, handling, and destruction rules

## Evidence Baseline

Verified on 2026-08-16:

- Repository commit: `96a473c`
- Branch: `main`, synchronized with `origin/main`
- Automated tests: 505 passed, 1 skipped, 0 failed
- Live Azure OpenAI test: intentionally disabled
- CI: Bicep validation, restore, release build, tests, and dependency audit
- CodeQL: security-extended analysis on changes and weekly
- Repository: secret scanning, push protection, and Dependabot
- Supply-chain inventory: SPDX 2.3 SBOM

## References

- NIST SP 800-53 Revision 5:
  https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final
- NIST SP 800-53B:
  https://csrc.nist.gov/pubs/sp/800/53/b/upd1/final
- NIST SP 800-171 Revision 3:
  https://csrc.nist.gov/pubs/sp/800/171/r3/final
- NIST SP 800-171A Revision 3:
  https://csrc.nist.gov/pubs/sp/800/171/a/r3/final
- NIST SP 800-218 SSDF Version 1.1:
  https://csrc.nist.gov/pubs/sp/800/218/final

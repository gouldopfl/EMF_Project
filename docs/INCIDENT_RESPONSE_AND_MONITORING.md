# EMF Incident Response and Continuous Monitoring Baseline

**Status:** Draft
**Date:** 2026-08-16
**Operational owner:** Unassigned
**Approval authority:** Unassigned

## Purpose

This document establishes the minimum incident-response and continuous-monitoring
baseline for an EMF deployment. It supports preparation, detection, response,
recovery, evidence preservation, and improvement activities.

The baseline aligns with NIST SP 800-61 Revision 3 and the Incident Response,
Audit and Accountability, and Continuous Monitoring controls in NIST SP 800-53
Revision 5.

This document does not establish legal or regulatory compliance. Before
production use, the deploying organization must assign accountable personnel,
approve reporting requirements, define response times, and tailor this plan to
the applicable authorization boundary.

## Scope

The baseline applies to:

- EMF application services and workflows
- protected artifacts and encrypted envelopes
- authorization and classification decisions
- intelligence providers and agent execution
- security audit records and audit persistence
- Azure Key Vault and related Azure infrastructure
- source control, CI, dependencies, and release artifacts
- backups, recovery material, and administrative tooling

It applies to suspected and confirmed events affecting confidentiality,
integrity, availability, privacy, authorization, provenance, or auditability.

## Production Preconditions

Before processing PII, PHI, or CUI, the deploying organization must:

- assign the incident-response roles defined below
- define severity-specific response and escalation times
- approve notification and reporting requirements
- provide a monitored reporting channel independent of EMF
- deploy centralized, access-controlled, integrity-protected audit collection
- define audit retention and privacy-minimization requirements
- authorize a least-privilege identity for continuous monitoring
- document emergency access and credential-revocation procedures
- approve evidence storage and chain-of-custody procedures
- conduct and record an incident-response exercise

## Roles and Responsibilities

| Role | Minimum responsibility |
|---|---|
| Approval authority | Approves the plan, risk decisions, external reporting, and return to service |
| Incident commander | Coordinates response, decisions, assignments, status, and closure |
| Security lead | Directs technical analysis, containment, evidence preservation, and eradication |
| System owner | Assesses mission impact and approves system recovery actions |
| Data or privacy owner | Assesses protected-information exposure and notification obligations |
| Communications or legal lead | Coordinates authorized internal and external communications |
| Evidence custodian | Preserves evidence, hashes, access history, and chain of custody |
| Recorder | Maintains the incident timeline, decisions, actions, and supporting evidence |

One person may hold multiple roles in a small deployment, but approval,
technical response, and evidence-handling responsibilities must remain explicit.

## Incident Classification

The incident commander assigns an initial severity and revises it as evidence
changes.

### Severity 1 — Critical

Examples include:

- suspected compromise or loss of a production encryption key
- confirmed unauthorized disclosure of PII, PHI, CUI, or authentication material
- authorization bypass affecting protected resources
- destructive activity, ransomware, or widespread service compromise
- loss of audit integrity during suspected malicious activity
- compromise of a privileged production identity or release pipeline

### Severity 2 — High

Examples include:

- credible unauthorized access without confirmed protected-data disclosure
- repeated or coordinated authorization denials suggesting active attack
- material Azure Policy drift or public exposure of a protected service
- security audit failure affecting a production workload
- workflow replay, checkpoint manipulation, or artifact-integrity failure
- untrusted intelligence output causing or attempting unauthorized action

### Severity 3 — Moderate

Examples include:

- isolated policy violations with no evidence of exploitation
- suspicious provider errors, destination changes, or anomalous cancellations
- dependency or code-scanning findings requiring investigation
- monitoring gaps that do not immediately affect protected processing

### Severity 4 — Low

Examples include:

- unsuccessful low-risk probes
- informational findings
- documentation or configuration discrepancies with no active exposure

Severity does not replace applicable reporting rules. The deploying
organization must document all contractual, statutory, regulatory, customer,
law-enforcement, and authorizing-official notification requirements.

## Response Lifecycle

### Prepare

- maintain current architecture, data-flow, asset, identity, and contact records
- preserve tested backup, key-recovery, and emergency-access procedures
- define approved tools, evidence repositories, and communication channels
- train assigned responders and exercise this plan
- validate that monitoring failures cannot silently permit protected processing

### Detect and Analyze

- record the first observation in UTC
- assign an incident identifier, commander, severity, and affected boundary
- distinguish an event, vulnerability, policy violation, and confirmed incident
- correlate audit records using subject, resource, destination, outcome,
  correlation ID, classification ID, provider operation ID, and timestamps
- identify affected artifacts, workflows, providers, identities, keys, and hosts
- preserve evidence before containment changes when safety permits

### Respond

- contain affected identities, providers, workflows, endpoints, and resources
- revoke or rotate compromised credentials and keys through approved procedures
- preserve original encrypted envelopes and audit records
- block unauthorized destinations and fail protected processing closed
- eradicate the verified cause and record every material action and decision
- make only authorized notifications using approved channels

### Recover

- restore from verified artifacts and approved recovery points
- validate authorization, encryption, audit, provider, and policy controls
- increase monitoring during the defined observation period
- obtain approval before returning protected processing to service
- retain recovery evidence and unresolved risks

### Improve

- document root cause, contributing conditions, impact, and response effectiveness
- create tracked corrective actions with owners and due dates
- update tests, controls, threat models, procedures, and training
- verify corrective actions and record formal closure or risk acceptance

## Continuous-Monitoring Sources

At minimum, production monitoring must cover:

- EMF `SecurityAuditRecord` outcomes and structured facts
- audit-sink availability, write failures, storage capacity, and migration errors
- authorization denials and authorization-service failures
- Key Vault access, key lifecycle, deletion protection, and network configuration
- Azure Policy assignment drift and reported noncompliance
- identity, role-assignment, privileged-access, and authentication changes
- intelligence-provider destination, model, version, operation, and failure data
- workflow checkpoint, replay, integrity, and recovery events
- GitHub code scanning, dependency alerts, workflow failures, and release changes
- host, database, backup, network, and service-health telemetry

## Minimum Alert Conditions

The production monitoring design must alert on:

- any audit-sink write failure for protected processing
- any protected operation that succeeds without its required audit record
- unexpected changes to policy assignments, enforcement modes, or effects
- Azure Policy noncompliance affecting Key Vault safeguards
- disabled public-network, deletion-protection, RBAC, or key-expiration controls
- unexpected provider destinations, engines, versions, or operation identifiers
- repeated `Denied` or `Failed` outcomes above an approved threshold
- integrity, authentication, envelope-version, or decryption failures
- attempted workflow replay or invalid checkpoint transitions
- privileged-role grants, key operations, or security-setting changes
- disabled monitoring, missing expected telemetry, or retention failure
- critical code-scanning, dependency, secret-scanning, or release-integrity findings

Thresholds, evaluation windows, notification routes, and responsible owners must
be approved and recorded before production use. Missing telemetry is itself a
monitoring event and must not be treated as evidence of compliance.

## EMF Audit Evidence

`SecurityAuditRecord` provides:

- operation
- resource type and identifier
- subject identifier
- authorization decision
- provider destination
- outcome
- UTC occurrence time
- structured facts

Current intelligence audit facts may include:

- correlation ID
- protection-classification ID
- input artifact identifiers
- agent ID
- engine name and version
- execution start and completion times
- provider operation ID

Audit facts must not contain plaintext protected content, secrets, credentials,
encryption key material, access tokens, or unnecessary personal information.

The SQLite audit sink is an application-level source, not a complete production
security-information and event-management system. Production deployments must
forward or collect required records into centralized, access-controlled,
integrity-protected storage.

## Evidence Preservation

Responders must:

1. record collection time, collector, source, method, and incident identifier
2. preserve the original evidence without modification
3. create working copies for analysis
4. calculate and record an approved cryptographic hash when technically feasible
5. preserve UTC timestamps, time-source information, and relevant clock offsets
6. restrict access to authorized responders
7. record each evidence transfer, access, transformation, and disposition
8. retain relevant configuration, logs, audit records, alerts, and command output
9. preserve affected encrypted envelopes in their original form
10. follow approved retention, legal-hold, privacy, and destruction requirements

Do not place protected content, credentials, keys, tokens, or unrestricted audit
exports in source-control issues, chat systems, or ordinary email.

## Incident Record

Each incident record must include:

- incident identifier and title
- detection source and initial UTC timestamp
- reporter and assigned roles
- current severity and rationale
- affected systems, data classes, identities, resources, and environments
- known and suspected indicators
- timeline of evidence, decisions, actions, and notifications
- containment, eradication, and recovery status
- evidence inventory and storage location
- impact assessment and notification decisions
- corrective actions, owners, due dates, and verification evidence
- closure approval or documented residual-risk acceptance

## Communications and Reporting

Only authorized personnel may communicate externally about an incident.
Responders must use an independent channel if EMF, its identity system, or its
normal communication path may be compromised.

The approval authority, legal lead, privacy owner, contracting authority, and
customer determine applicable reporting recipients and deadlines. This baseline
does not assume that one notification deadline applies to every deployment.

No responder may delay immediate safety or containment action solely to complete
documentation. The action and rationale must be recorded as soon as practicable.

## EMF Response Priorities

Responders must prioritize:

- audit failure: stop protected processing that cannot be audited
- key compromise: restrict the key and identity, preserve evidence, then rotate
- protected-data exposure: contain the path and involve the data or privacy owner
- prompt injection: quarantine input and output and disable the affected route
- workflow replay: halt resume, preserve checkpoints, and verify external effects
- policy drift: preserve configuration evidence before approved remediation

Original encrypted envelopes, audit records, checkpoints, and provider metadata
must be preserved when relevant. Protected content, credentials, keys, and
tokens must not be copied into ordinary tickets, chats, or email.

## Exercises and Closure

Conduct and record an exercise before production protected-data processing and
at least annually thereafter. Exercises must cover audit failure, key
compromise, protected-data exposure, evidence preservation, communications,
recovery, and approval to return to service.

An incident closes only after containment, recovery, notification decisions,
evidence retention, corrective-action ownership, and residual-risk acceptance
are documented and approved.

## Known Gaps

As of 2026-08-16:

- operational roles and response times are unassigned
- centralized production audit collection is not deployed
- alert thresholds and notification routes are not approved
- the local hash chain requires external chain-head anchoring and centralized collection
- no production monitoring identity is approved
- no incident-response exercise has been recorded

## References

- NIST SP 800-61 Revision 3:
  https://csrc.nist.gov/pubs/sp/800/61/r3/final
- NIST SP 800-53 Revision 5:
  https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final
- `docs/THREAT_MODEL.md`
- `docs/NIST_CONTROL_MAPPING.md`
- `docs/PROTECTED_BACKUP_RECOVERY.md`
- `docs/AZURE_KEY_MANAGEMENT_OPERATIONS.md`

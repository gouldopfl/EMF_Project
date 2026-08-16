# EMF Threat Model

## Status

Draft engineering threat model — 2026-08-16

## Purpose

This document identifies threats to EMF assets, trust boundaries, data
flows, and security operations. It records existing mitigations, residual
risk, and required security work.

This is not a penetration-test report, risk acceptance, certification,
security authorization, or claim that every threat has been identified.

## Method

The model uses threat-source, vulnerability, likelihood, impact, and
residual-risk concepts from NIST SP 800-30 Revision 1. It also uses
data-centric principles from draft NIST SP 800-154.

Because SP 800-154 remains an initial public draft, this document does not
claim conformance with it.

## Scope

The current scope includes EMF application code, protected artifact storage,
security auditing, workflow persistence and recovery, Azure Key Vault or
Managed HSM integration, Azure OpenAI integration, and repository automation.

Production Azure infrastructure and organizational procedures are included
only as unverified external dependencies.

## Protected Assets

- Artifact and Evidence plaintext content
- Encrypted artifact envelopes and authentication metadata
- Data-encryption keys while temporarily present in application memory
- Key-encryption key identifiers and historical key references
- Protection classifications and authorization decisions
- User, service, agent, and workflow execution identities
- Security audit records and correlation metadata
- Workflow state, checkpoints, retry history, and recovery decisions
- Evidence provenance, integrity hashes, and source relationships
- Intelligence requests, prompts, responses, and derived results
- Provider, deployment, model-version, and operation metadata
- Azure endpoints, deployment names, and security configuration
- Source code, dependencies, CI configuration, and build outputs

## Security Objectives

- Deny access and external processing unless explicitly authorized.
- Preserve artifact confidentiality, integrity, provenance, and availability.
- Prevent a provider fallback from weakening protection policy.
- Keep production key-encryption keys outside application-controlled memory.
- Make security-relevant activity attributable and reconstructable.
- Preserve durable state during failure, cancellation, retry, and recovery.
- Reject corrupted, incomplete, unsupported, or unsafe security data.
- Prevent prompts, responses, plaintext, and secrets from entering logs.
- Keep provider-specific dependencies outside platform and domain contracts.
- Detect vulnerable dependencies and security weaknesses before integration.

## Trust Boundaries

1. **Caller to EMF:** Requests cross from a user, service, or agent into the
   application and require validated identity, permission, and input.
2. **Domain to security services:** Domain and orchestration components rely
   on protection policy, authorization, encryption, and auditing.
3. **Application to content storage:** Plaintext becomes an encrypted envelope
   before crossing into the physical artifact store.
4. **Application to local persistence:** Audit records, workflow state,
   checkpoints, and recovery decisions cross into SQLite stores.
5. **Application to Azure key management:** Wrapped keys and versioned key
   references cross to Azure Key Vault or Managed HSM.
6. **Application to Azure OpenAI:** Authorized intelligence content crosses
   to an external semantic-intelligence provider.
7. **Development to production providers:** Deterministic development
   providers must never become fallback paths for protected production data.
8. **Repository to build services:** Source, dependencies, workflows, and
   outputs cross GitHub Actions and external package-supply boundaries.
9. **Operators to Azure control plane:** Deployment configuration, managed
   identities, permissions, networking, logging, and keys are externally set.

## Principal Data Flows

### Protected Artifact Storage

Caller input is authorized and classified, encrypted with a fresh
data-encryption key, wrapped through the configured key provider, serialized
as an encrypted envelope, and written to physical storage.

### Artifact Key Rewrapping

An existing envelope is validated, its data-encryption key is unwrapped with
the historical key, and that key is wrapped with the current key. Ciphertext
and artifact identity remain unchanged, and failed replacement preserves the
original envelope.

### Intelligence Execution

A request is authorized, classified, routed only to an eligible provider,
executed with cancellation and timeout controls, normalized with provider
metadata, and audited with correlation and outcome information.

### Workflow Execution and Recovery

Activities record durable progress and checkpoints. Interrupted execution is
evaluated by recovery policy, which records an explicit resume, retry, review,
failure, or abandonment decision before execution continues.

## Threat Sources

- External attacker attempting unauthorized access or data disclosure
- Malicious or careless insider with legitimate system access
- Compromised user, service, workload, or administrator identity
- Overprivileged managed identity or incorrectly configured Azure role
- Compromised dependency, build action, package source, or developer account
- Compromised, misconfigured, unavailable, or policy-ineligible provider
- Malformed, corrupted, replayed, or adversarially crafted stored data
- Prompt injection or hostile content embedded in intelligence input
- Accidental disclosure through logs, diagnostics, exports, or backups
- Infrastructure failure, resource exhaustion, throttling, or network outage
- Defective code or configuration that bypasses an intended security boundary

## Risk Rating Method

Each threat records qualitative likelihood, impact, and residual risk after
current repository-supported mitigations.

- **Likelihood — Low:** Requires unusual access or several unlikely failures.
- **Likelihood — Moderate:** Credible under foreseeable operating conditions.
- **Likelihood — High:** Expected without additional controls.
- **Impact — Low:** Limited and recoverable operational effect.
- **Impact — Moderate:** Material confidentiality, integrity, or availability
  effect requiring investigation and recovery.
- **Impact — High:** Protected-data disclosure, loss of trust, unrecoverable
  evidence damage, major outage, or regulatory consequence.
- **Residual risk — Low:** Existing controls substantially reduce the risk.
- **Residual risk — Moderate:** Useful controls exist, but important work
  remains.
- **Residual risk — High:** A material control or deployment decision is
  missing or unverified.

Ratings support prioritization only. Risk acceptance requires an authorized
owner and is outside this document.

## Threat Register

### TM-01 — Identity Spoofing or Credential Compromise

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** A forged or compromised user, workload, service, or
  administrator identity is accepted as an authorized subject.
- **Current mitigations:** Blank subjects and missing authorization contexts
  are denied; permissions are explicit; Azure adapters use managed identity.
- **Required work:** Integrate a production identity provider; validate tokens
  and audience; enforce MFA and least privilege; define identity lifecycle,
  privileged access, credential response, and recurring access reviews.

### TM-02 — Authorization or Classification Bypass

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** Moderate
- **Scenario:** Missing, incorrect, or manipulated permissions or protection
  classifications permit unauthorized access or external processing.
- **Current mitigations:** Authorization fails closed; protection policy
  participates in provider routing; unsupported or unauthorized requests are
  rejected; production failure does not fall back to Development.
- **Required work:** Define authoritative classification assignment, policy
  administration, separation of duties, configuration integrity, and verified
  enforcement coverage for every production entry point.

### TM-03 — Protected Content Disclosure

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** Plaintext, prompts, responses, derived content, or sensitive
  metadata are exposed through logs, diagnostics, exports, providers, backups,
  temporary files, or incorrectly protected storage.
- **Current mitigations:** Artifact content is envelope-encrypted; external
  routing is classification-aware; tests require prompts and responses to be
  absent from audit and provider metadata.
- **Required work:** Enforce log redaction and data minimization; protect
  backups, temporary data, exports, and metadata; verify provider data handling,
  retention, geography, networking, and incident-notification obligations.

### TM-04 — Encrypted Envelope Tampering

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** Moderate
- **Scenario:** Ciphertext, authentication data, wrapped keys, key identifiers,
  algorithms, or serialized envelope structure are modified or substituted.
- **Current mitigations:** AES-GCM authenticates ciphertext; corruption is
  rejected; key versions are explicit; versioned authenticated context binds
  new encrypted content to its artifact identity; substitution and replay
  tests verify rejection; rewrapping preserves ciphertext and context binding.
- **Required work:** Approve, implement, and exercise the documented
  [protected backup and recovery](PROTECTED_BACKUP_RECOVERY.md) procedure.

### TM-05 — Key Compromise, Loss, or Unavailability

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** An attacker gains key-use permission, a required historical key
  is deleted, or Azure key services become unavailable.
- **Current mitigations:** Production key-encryption keys remain provider
  managed and non-exportable; key versions are preserved; data-encryption keys
  are random and cleared from memory; rewrapping supports rotation; Azure vault
  endpoints must be absolute HTTPS root URIs without embedded credentials.
- **Required work:** Implement and exercise the documented
  [Azure key-management operations](AZURE_KEY_MANAGEMENT_OPERATIONS.md)
  baseline, including infrastructure enforcement and authorized recovery.

### TM-06 — Audit Loss, Tampering, or Repudiation

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** Security events are omitted, altered, deleted, flooded, or made
  unattributable, preventing reconstruction and accountability.
- **Current mitigations:** Structured records include subject, operation,
  resource, decision, outcome, time, destination, and facts; SQLite migrations
  and sink-failure behavior are tested. A draft incident-response and monitoring
  baseline defines monitoring sources, alert conditions, evidence handling, and
  response activities.
- **Required work:** Approve and exercise the plan; assign operational ownership;
  centralize logs in access-controlled, integrity-protected storage; and define
  retention, time synchronization, review, alerting, capacity, privacy
  minimization, failure recovery, and administrative separation.

### TM-07 — Workflow State Tampering or Replay

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** Checkpoints, retry history, execution status, activity identity,
  or recovery decisions are altered or replayed to skip work, duplicate work,
  conceal failure, or produce incorrect evidence state.
- **Current mitigations:** Persisted state is authoritative; activity identity
  and workflow versions are stable; recovery decisions are explicit; completed
  work is not unnecessarily repeated.
- **Required work:** Authenticate and authorize state changes; add optimistic
  concurrency, integrity verification, protected backups, replay detection,
  administrative audit, and recovery testing.

### TM-08 — Unsafe Provider Selection or Fallback

- **Likelihood:** Low
- **Impact:** High
- **Residual risk:** Moderate
- **Scenario:** Protected content is routed to an unauthorized provider,
  deployment, region, or Development implementation.
- **Current mitigations:** Routing considers protection classification;
  unsupported and unauthorized requests fail closed; provider failure does not
  fall back to Development; normalized metadata records provider selection.
- **Required work:** Protect routing configuration; separate administration;
  verify production composition; monitor routing decisions; test deployment,
  region, classification, and failure combinations in an authorized environment.

### TM-09 — Prompt Injection or Untrusted Intelligence Output

- **Likelihood:** High
- **Impact:** High
- **Residual risk:** High
- **Scenario:** Hostile content manipulates an intelligence operation, causes
  unauthorized disclosure, introduces unsupported conclusions, or produces
  derived content that is mistaken for verified Evidence.
- **Current mitigations:** Capabilities are typed and provider-neutral; source,
  provider, model, correlation, warnings, and review metadata are preserved;
  promotion into Evidence requires explicit provenance.
- **Required work:** Treat all content and model output as untrusted; isolate
  instructions from source data; restrict tools and destinations; add output
  validation, content limits, human review, adversarial tests, and promotion
  policy enforcement.

### TM-10 — Software Supply-Chain Compromise

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** Moderate
- **Scenario:** A dependency, GitHub action, package source, developer account,
  workflow, or build output is maliciously modified.
- **Current mitigations:** Actions are pinned by commit SHA; CI builds and tests
  changes; CodeQL, NuGet auditing, Dependabot, secret scanning, push protection,
  and an SPDX 2.3 SBOM are enabled.
- **Required work:** Add signed releases, provenance attestations, protected
  approvals, dependency-source policy, artifact retention, supplier review,
  incident procedures, and deployment-time integrity verification.

### TM-11 — Service Denial or Resource Exhaustion

- **Likelihood:** Moderate
- **Impact:** Moderate
- **Residual risk:** Moderate
- **Scenario:** Large inputs, repeated requests, throttling, dependency outage,
  audit failure, network interruption, or key-provider failure prevents useful
  work or leaves operations incomplete.
- **Current mitigations:** Cancellation and timeouts propagate; provider
  failures are explicit; workflows checkpoint durable progress and support
  deterministic recovery.
- **Required work:** Add rate and size limits, bounded retries, backoff, circuit
  breaking, capacity monitoring, quotas, load testing, health checks, service
  objectives, alerts, and documented degraded-operation procedures.

### TM-12 — Insecure Deployment or Configuration Drift

- **Likelihood:** Moderate
- **Impact:** High
- **Residual risk:** High
- **Scenario:** Azure identities, endpoints, networks, keys, providers, logging,
  or storage are deployed with unsafe settings or drift from an approved state.
- **Current mitigations:** Required options are validated; managed identity
  avoids embedded API keys; ADRs define production boundaries; the Azure Key
  Vault Bicep profile defines RBAC, private networking, diagnostics, deletion
  protection, and key rotation. A CI-validated Azure Policy baseline defines
  deny controls for unsafe Key Vault settings and audits private-link
  compliance. A read-only checker fails closed when assignments are missing,
  misconfigured, inaccessible, or report noncompliant resources.
- **Required work:** Review and deploy approved infrastructure and policy
  parameters; approve a monitoring identity; schedule compliance checks;
  define remediation; implement environment separation, deployment gates,
  configuration inventory, and periodic review.

## Treatment Priorities

### High Residual Risk

- TM-01: production identity and privileged-access governance
- TM-03: end-to-end protected-content handling
- TM-05: production key lifecycle and recovery
- TM-06: centralized, integrity-protected auditing
- TM-07: workflow-state integrity and replay protection
- TM-09: prompt-injection and untrusted-output controls
- TM-12: hardened infrastructure and drift control

### Moderate Residual Risk

- TM-02: classification and authorization administration
- TM-04: approve and exercise protected backup and recovery
- TM-08: production routing verification and monitoring
- TM-10: signed releases and supply-chain provenance
- TM-11: capacity, resilience, and degraded-operation controls

No residual risk listed here has been formally accepted.

## Review Triggers

Review and update this threat model:

- before any production use of PII, PHI, or CUI
- before adding or changing an external intelligence provider
- when a trust boundary, storage provider, or key provider changes
- when authentication, authorization, or classification policy changes
- after a security incident, penetration test, or material vulnerability
- when a high residual risk receives a new mitigation or acceptance decision
- at least annually while EMF remains under active development

Each review must record the date, scope, evidence, decisions, responsible
owner, and any change to likelihood, impact, or residual risk.

## References

- NIST SP 800-30 Revision 1:
  https://csrc.nist.gov/pubs/sp/800/30/r1/final
- Draft NIST SP 800-154:
  https://csrc.nist.gov/pubs/sp/800/154/ipd
- ADR-017: Protected and Regulated Information Boundary
- ADR-018: Security Key Management Boundary
- ADR-019: Production Envelope Encryption
- ADR-020: Azure Key Management Adapter Boundary
- ADR-021: Artifact Content Protection Boundary
- ADR-022: Artifact Envelope Key Rewrapping Lifecycle
- ADR-023 through ADR-025: Workflow Recovery Boundaries
- ADR-026: Intelligence Services and Agent Boundary
- ADR-027: Initial Production Intelligence Provider
- `docs/INCIDENT_RESPONSE_AND_MONITORING.md`
- `docs/NIST_CONTROL_MAPPING.md`

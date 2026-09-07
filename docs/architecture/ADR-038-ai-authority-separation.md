# ADR-038: AI Authority Separation

## Status

Accepted

## Date

2026-09-07

## Context

ADR-026 establishes EMF's provider-neutral intelligence boundary, treats intelligence output as derived work product, requires provenance and review for promotion, and prevents agents from silently modifying source Evidence, adjudication records, or regulatory authority.

As AI systems become more capable, reasoning capability may advance faster than operators can independently understand every internal step. Capability must therefore remain distinct from authority.

## Decision

Intelligence capability shall not confer operational authority.

AI providers, models, agents, and intelligence capabilities shall operate only within authority explicitly granted by independently enforced EMF policy, authorization, workflow, and security boundaries.

A more capable provider, model, deployment, version, or agent shall not automatically receive broader permissions than the component it replaces.

## Consequential Actions

Consequential actions include operations that materially affect:

- source Artifacts or Evidence
- provenance or integrity metadata
- authoritative domain or adjudication records
- workflow control state
- authorization or permission state
- cryptographic keys or protected-content state
- audit records
- persistent deletion or destructive modification
- promotion or publication of intelligence output
- externally visible decisions or submissions

Intelligence may recommend or prepare such actions without automatically being authorized to execute them.

## Independent Enforcement

Authorization for consequential actions shall be enforced outside the intelligence provider or model producing the recommendation. A model shall not be treated as the authoritative evaluator of its own permission.

Where applicable, execution shall require independently evaluated acting identity, resource identity, requested operation, protection classification, workflow state, policy requirements, validation requirements, and required human review or approval.

Failure to establish required authority shall fail closed.

## Tool and Agent Authority

Access to a tool does not imply unrestricted authority to use it.

Agent and tool capabilities shall be scoped to the minimum operations and resources required for their objective. Read, analyze, classify, summarize, recommend, modify, publish, delete, and security-administrative capabilities shall remain distinguishable permissions.

An agent shall not escalate its authority because another capability, provider, model, or tool is available.

## Human Authority

Where human authorization is required, it shall represent a real control boundary rather than retrospective attribution. Required review or approval shall occur before the consequential operation, and the approving identity and role shall be preserved when applicable.

## Provider and Model Changes

Changing providers, deployments, models, versions, reasoning capability, or agent implementations shall not silently expand authority. New capabilities requiring additional authority shall require explicit configuration and verification.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- intelligence output cannot become authoritative Evidence without explicit promotion
- unauthorized consequential actions fail closed
- provider or model replacement does not broaden permissions
- tool availability does not bypass authorization
- agents cannot silently modify protected source records
- required human approval is enforced before controlled operations
- failed authorization does not produce partial persistent changes
- audit records identify the authority under which consequential actions occur

## Consequences

Benefits include safer adoption of increasingly capable intelligence, independent control of model upgrades and permissions, explicit human and organizational authority, and a narrower blast radius for mistakes or compromise.

Costs include additional validation or approval for some actions, deliberate tool and agent permission design, and continued policy and verification work as capabilities expand.

## Rejected Alternatives

### Allow capable models to determine their own authority

Rejected because reasoning capability is not an authorization mechanism and a model must not certify its own permission.

### Grant all available tools to every agent

Rejected because unnecessary tool access expands the impact of mistakes, compromise, or unintended behavior.

### Automatically expand permissions when models improve

Rejected because intelligence capability and operational authority are independent architectural concerns.

### Rely only on retrospective audit

Rejected because audit can reconstruct an unauthorized action but cannot prevent it.

## References

- ADR-017: Protected and Regulated Information Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-028: Tamper-Evident Security Audit Storage
- ADR-031: Resource-Neutral Authorization Requests
- ADR-037: Deployment Profiles and Operational Simplicity

## Architectural Principle

AI may advise broadly while acting narrowly.

Capability does not imply authority, and consequential actions remain subject to independently enforced EMF controls.

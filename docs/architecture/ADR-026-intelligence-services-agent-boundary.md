# ADR-026: Intelligence Services and Agent Boundary

## Status

Accepted

## Date

2026-08-14

## Context

ADR-012 establishes EISL as the provider-neutral intelligence boundary between
Domain Extensions and specific AI or intelligence providers.

ADR-016 establishes that stateful agents and coherent agent subsystems own
their persistence semantics, provider-specific schemas, migrations, and
component-scoped migration ledgers.

ADR-017 requires provider selection and information disclosure to satisfy
applicable protection policy before protected information is transmitted to an
intelligence provider.

EMF now requires a concrete boundary for intelligence capabilities and agents
before provider adapters, agent implementations, or durable agent state are
introduced.

Without this boundary, Domain Extensions could become coupled to individual AI
providers, agent state could be confused with workflow or domain state, and
protected evidence could be transmitted without consistent authorization,
audit, provenance, or provider-selection controls.

## Decision

EISL shall be the provider-neutral boundary through which EMF requests
intelligence capabilities.

Consumers shall request a capability rather than an AI engine, model, cloud
service, or named provider.

Provider SDK types, model-specific request objects, credentials, and transport
details shall remain inside provider adapters.

The application composition root shall configure the available intelligence
providers, capability routing, protection policy, and agent implementations
during startup.

## Core Concepts

A Capability is a stable intelligence operation requested by an EMF consumer.

An Agent is an EMF component that coordinates one or more capabilities toward
a defined objective. An agent may be stateless or may own durable operational
state.

An AI Engine is a provider-specific technology used to implement one or more
capabilities. It is an adapter implementation detail rather than the
architectural identity of a capability or agent.

## Capability Contracts

Provider-neutral capability requests shall identify:

- the requested capability
- the acting identity or execution context
- authorized input resources or content
- applicable protection classification
- domain context required for the operation
- cancellation, timeout, and correlation information

Provider-neutral results shall expose:

- the capability outcome
- generated or derived content
- grounding or source references when applicable
- warnings, limitations, and review requirements
- provider execution metadata needed for traceability
- the correlation identity needed for audit

Capability contracts shall not expose provider SDK types or require consumers
to name a provider or model.

## Evidence and Traceability

Intelligence output is derived work product. It does not automatically become
platform Evidence or an authoritative domain fact.

When intelligence output is promoted into Evidence or a domain record, EMF
shall preserve provenance linking the result to:

- the input Artifacts or Evidence
- the capability requested
- the agent or execution context
- the provider and model or engine version
- the operation time
- applicable human review or approval

An agent shall not silently modify source Evidence, domain adjudication
records, or regulatory authority.

## Protection and Provider Routing

Provider routing shall consider both capability support and applicable
protection policy.

A configured provider may receive protected information only when disclosure
to that provider is authorized for the request and classification involved.

A Domain Extension or agent shall not bypass EISL to transmit protected
information directly to an AI engine.

Fallback shall fail closed. Failure or unavailability of an authorized provider
shall not cause automatic disclosure to a less protective provider.

Runtime routing may select among providers configured during startup, but the
consumer shall continue to request only the provider-neutral capability.

## Agent State and Persistence

Stateless agents do not require agent-owned persistence.

A stateful agent or coherent agent subsystem shall own:

- its state contracts and persistence semantics
- its provider-specific persistence adapter
- its schema and versioned migrations
- its component-scoped migration ledger
- compatibility checks for its stored state version

Agent state shall remain distinct from:

- workflow execution and checkpoint state
- platform Artifact and Evidence records
- Domain Extension records
- provider conversation or session identifiers

An agent may reference platform or domain identities, but it shall not own or
migrate tables belonging to another component.

Agent persistence providers shall be selected and initialized during
application composition. Provider selection or schema migration shall not
occur during an agent operation.

## Authorization and Audit

Intelligence operations involving protected information or durable agent state
shall be subject to applicable authorization policy.

Auditable facts shall include:

- the capability and operation performed
- the acting identity or execution context
- the input resource identities
- the selected provider and engine or model version
- the protection classification and routing decision
- the agent identity when an agent coordinated the operation
- the operation time, correlation identity, and outcome

Audit records shall not contain plaintext protected content, credentials, raw
provider secrets, or complete prompts and responses unless a separately
authorized retention policy explicitly requires them.

## Failure and Cancellation

Cancellation, timeout, authorization denial, provider failure, invalid output,
and policy rejection shall be represented explicitly.

An operation shall not be reported as successful when required audit recording,
provenance capture, output validation, or durable state persistence fails.

Retries shall preserve correlation and idempotency semantics appropriate to the
capability or agent operation.

## Verification Requirements

Tests shall verify:

- consumers can request capabilities without provider-specific types
- capability routing selects only configured and policy-permitted providers
- protected content is rejected when no authorized provider is available
- fallback does not weaken protection policy
- results preserve required provider and provenance metadata
- cancellation and provider failures are propagated
- security-relevant outcomes are audited
- stateless agents require no durable state
- stateful agent migrations affect only agent-owned schema objects
- repeated agent-state initialization is safe
- a newer unsupported agent-state version is rejected

## Consequences

Benefits:

- Domain Extensions remain independent from AI providers
- providers and models can change without changing consumer contracts
- protection policy participates directly in provider routing
- agent state has explicit ownership and migration boundaries
- intelligence output remains traceable to source evidence and execution
- security-relevant intelligence operations can be reconstructed through audit
- stateless and stateful agents can coexist without forcing one persistence
  model

Tradeoffs:

- capability contracts require careful provider-neutral design
- composition must configure providers, routing, protection, and auditing
- provider metadata must be normalized for traceability
- stateful agents require separate persistence and migration work
- protected requests may fail when no authorized provider is available
- promotion of intelligence output into Evidence requires explicit provenance
  and review handling

## Rejected Alternatives

### Allow Domain Extensions to call AI providers directly

Rejected because it would couple domain logic to provider SDKs and bypass
consistent protection, routing, provenance, and audit controls.

### Treat every AI engine as an agent

Rejected because an AI engine is a provider implementation, while an agent is
an EMF component that coordinates capabilities toward an objective.

### Store agent state in platform workflow tables

Rejected because workflow recovery state and agent operational state have
different ownership, lifecycle, compatibility, and migration semantics.

### Treat generated output as Evidence automatically

Rejected because intelligence output is derived work product and requires
explicit provenance, validation, and promotion.

### Select any available provider after a policy failure

Rejected because availability does not authorize disclosure and fallback must
not weaken protection requirements.

### Require every agent to persist state

Rejected because stateless agents should not incur unnecessary schema,
migration, retention, or recovery obligations.

## Architectural Principle

EMF consumers request provider-neutral intelligence capabilities.

Agents coordinate those capabilities toward defined objectives.

AI engines implement capabilities behind EISL adapters.

Protection, provenance, audit, and state ownership remain explicit at every
boundary.

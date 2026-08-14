# ADR-027: Initial Production Intelligence Provider

## Status

Accepted

## Date

2026-08-14

## Context

ADR-026 establishes EISL as the provider-neutral boundary for intelligence
capabilities, agents, provider routing, protection policy, traceability, and
promotion of intelligence output into Evidence.

The deterministic provider in EMF.Intelligence.Development verifies those
boundaries and supports repeatable tests and offline development. It is not a
production semantic-intelligence provider.

EMF now requires its first production provider adapter. The selection must
preserve the protected-information controls in ADR-017, integrate with the
existing Azure security architecture, avoid embedded credentials, and retain
provider-neutral consumers.

## Decision

Azure OpenAI in Microsoft Foundry shall be EMF's first production
intelligence provider.

The adapter shall be owned by a dedicated project:

`EMF.Intelligence.AzureOpenAI`

The adapter shall implement existing EISL capability contracts. Provider SDK
types, request types, response types, deployment identifiers, and transport
details shall not escape the adapter boundary.

The initial adapter shall support only explicitly implemented capabilities.
Unsupported capabilities shall fail rather than silently changing provider,
model, or behavior.

EMF.Intelligence.Development shall remain available for deterministic tests,
offline development, and laboratory verification. It shall not be an
automatic fallback for a protected production request.

## Authentication

Production workloads shall authenticate through Microsoft Entra ID using an
Azure managed identity.

API keys shall not be embedded in source code, configuration files, command
arguments, logs, audit records, or persisted provider metadata.

Local development may use an authorized developer identity through the same
token-based credential chain. Production authorization shall use least-
privilege Azure role assignments scoped to the configured provider resource.

The adapter configuration shall identify the endpoint and deployment but
shall not contain reusable authentication secrets.

## Protection and Data Handling

Provider routing shall authorize both the Azure OpenAI provider and the
configured protection classification before any protected content leaves
EMF.

An available deployment does not imply authorization to disclose content to
it. When no authorized deployment supports the requested capability and
classification, execution shall fail closed.

Provider fallback shall not weaken protection policy. A failed or unavailable
Azure deployment shall not cause automatic routing to the Development
provider or another provider with different disclosure authorization.

Prompts and responses shall not be written to application logs, security audit
records, exception messages, or provider metadata. Evidence promotion remains
a separate, explicit, review-gated operation.

## Configuration and Routing

Configuration shall provide:

- the Azure OpenAI endpoint
- the deployment name
- the provider identifier
- supported capability identifiers
- authorized protection classifications
- request timeout and retry limits

The composition root shall register the adapter and its routing policy.
Consumers shall continue requesting only provider-neutral capabilities.

Configuration errors, missing deployments, unsupported capabilities, and
unauthorized classifications shall fail during composition or before provider
invocation whenever possible.

## Result Metadata

Every successful or failed provider execution shall preserve normalized
metadata sufficient for traceability, including:

- EISL capability identifier
- provider identifier
- deployment or engine name
- model or engine version when available
- provider operation identifier when available
- correlation identifier
- execution start and completion times
- warnings and review requirements

Provider metadata shall not contain credentials, full prompts, full responses,
or protected source content.

## Failure and Cancellation

Cancellation and timeout shall propagate through the existing EISL contracts
to the Azure SDK operation.

Authentication failure, authorization denial, throttling, timeout, invalid
provider output, and transport failure shall remain distinguishable adapter
failures. They shall not produce a successful capability result.

Retry behavior shall be bounded and shall not bypass cancellation, protection
policy, or audit requirements.

## Verification Requirements

Automated tests shall verify that:

- provider SDK types remain inside EMF.Intelligence.AzureOpenAI
- managed-identity credentials are used without embedded API keys
- configured capabilities produce normalized EISL results
- unauthorized classifications are rejected before provider invocation
- unsupported capabilities fail closed
- provider failure does not route protected content to Development
- cancellation and timeout propagate
- provider, deployment, model, correlation, and timing metadata are preserved
- prompts and responses are absent from logs and audit metadata
- adapter tests can run without contacting Azure

Live integration tests shall be separately identified and shall run only when
an authorized Azure endpoint and identity are explicitly configured.

## Consequences

Positive consequences include:

- EMF gains a production semantic-intelligence provider
- Azure identity and access controls remain aligned with existing security
  architecture
- consumers and Domain Extensions remain provider-neutral
- deterministic development and production behavior remain clearly separated
- provider execution remains traceable through normalized metadata

Costs and constraints include:

- an Azure OpenAI resource and model deployment must be provisioned
- model and regional availability become deployment concerns
- inference introduces variable cost, latency, throttling, and service
  availability
- live integration testing requires an authorized Azure identity and resource
- protected classifications require explicit routing authorization

## Rejected Alternatives

### Use the Development provider in production

Rejected because deterministic truncation and lexical extraction verify
architecture but do not provide production semantic intelligence.

### Allow API-key authentication

Rejected for production because reusable secrets would create avoidable
storage, rotation, disclosure, and audit risks.

### Call the Azure SDK directly from consumers

Rejected because it would expose provider types, deployments, and transport
details outside EISL and violate ADR-026.

### Fall back automatically after Azure failure

Rejected because availability does not authorize disclosure and fallback
could weaken protection policy.

### Implement durable agent state first

Rejected as the next increment because a real stateless provider adapter
validates the provider boundary without first introducing state schemas,
migrations, and compatibility concerns.

## References

- ADR-017: Protected and Regulated Information Boundary
- ADR-026: Intelligence Services and Agent Boundary
- Microsoft: Data, privacy, and security for Models sold by Azure
- Microsoft: Managed identity authentication for Azure OpenAI

## Architectural Principle

EMF may use Azure OpenAI as a production intelligence engine without allowing
Azure-specific concepts, credentials, or disclosure decisions to escape the
provider adapter boundary.

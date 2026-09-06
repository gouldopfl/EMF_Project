# ADR-036: Deferred Billing and AI Usage Accounting

## Status

Accepted

## Context

EMF may be used by individual veterans, Veterans Service Organizations,
accredited representatives, attorneys, sponsored programs, and other
organizations.

Those users may ultimately have different funding arrangements. Some use may
be free or subsidized, some may be funded by a sponsoring organization, and
some professional use may eventually be subscription- or usage-based.

AI inference also introduces variable provider cost. The cost depends on the
provider, model or deployment, capability, request size, response size, and
provider pricing in effect at the time of execution.

EMF does not currently need a billing system. Adding invoices, subscriptions,
payment processing, entitlements, or pricing rules now would introduce
commercial policy before the product and deployment models are mature.

If EMF waits to capture usage until billing is introduced, however, future
billing and sponsorship models would require reconstruction of historical
execution data or invasive changes to the intelligence boundary.

## Decision

EMF shall defer billing while preserving provider-neutral AI usage accounting.

Usage accounting and billing are separate responsibilities.

EMF may record normalized usage and cost-attribution metadata for an
intelligence execution without charging a user, creating an invoice, or
deciding who ultimately pays for that execution.

Billing, subscriptions, payment processing, pricing plans, sponsorship rules,
and commercial entitlements shall not be required by the current architecture.

## Usage Accounting

When available from the provider, normalized usage records may include:

- intelligence capability identifier
- provider identifier
- deployment or engine identifier
- model or engine version
- input, output, cached, or other provider-reported token or unit counts
- provider operation and correlation identifiers
- execution start and completion times
- provider-reported or EMF-calculated cost estimate
- currency and pricing-version information when known
- authorized accounting scope, such as tenant, organization, workload, or
  deployment

Provider-specific usage types shall remain inside the provider adapter.
Consumers shall use provider-neutral accounting records.

Usage accounting shall not store full prompts, full responses, credentials,
or protected source content merely for billing purposes.

## Billing Boundary

A usage record is not a bill.

The existence of measured usage shall not imply:

- that a veteran owes money
- that a professional user is subscribed to a paid plan
- that an organization has accepted sponsorship responsibility
- that a particular retail price applies
- that access should be denied when a commercial account is absent

Future billing services may consume normalized usage records and apply
separate pricing, sponsorship, subsidy, subscription, or entitlement policy.

Those commercial rules shall remain outside intelligence execution and domain
adjudication logic.

## Funding Models

The architecture shall remain capable of supporting future funding models such
as:

- free or subsidized individual veteran use
- VSO or nonprofit sponsorship
- state, federal, or institutional sponsorship
- organization-funded professional use
- individual professional subscriptions
- usage-based or capacity-based commercial plans

This ADR does not select or implement any of those models.

## Security and Privacy

Accounting identifiers shall follow the existing authorization and protected
information boundaries.

Usage records shall contain only the metadata necessary for operational,
financial, and audit purposes.

A billing or accounting component shall not gain access to protected evidence
content merely because it receives usage information.

Provider pricing credentials, payment credentials, and financial account
secrets shall not be stored in intelligence execution metadata.

## Verification Requirements

Automated tests shall verify, as usage accounting is implemented, that:

- provider-specific usage objects do not escape provider adapters
- normalized usage can be recorded without enabling billing
- missing provider usage data does not fabricate usage values
- protected prompts and responses are absent from accounting records
- accounting failures do not silently alter intelligence results
- commercial policy does not become a dependency of domain adjudication logic

## Consequences

### Benefits

- EMF can postpone commercial decisions without losing cost visibility.
- Free, sponsored, and professional deployment models can share one
  intelligence architecture.
- Future billing can be added without redesigning provider execution.
- AI cost can be analyzed by capability, provider, deployment, or authorized
  accounting scope.
- Veterans Claims remains independent of commercial policy.

### Tradeoffs

- Usage accounting requires durable metadata even before billing exists.
- Provider usage and pricing information may be incomplete or change over
  time.
- Cost estimates may differ from final provider invoices.
- Future billing policy will still require a separate architectural decision.

## Rejected Alternatives

### Implement billing immediately

Rejected because pricing, subscriptions, payment processing, sponsorship, and
entitlements are not required to validate EMF's evidence-management and
reviewer workflows.

### Ignore usage until billing is needed

Rejected because historical cost attribution would be unavailable and billing
would become tightly coupled to provider-specific execution details.

### Put billing logic inside the intelligence provider adapter

Rejected because adapters translate provider behavior; they do not own
commercial policy.

### Put billing rules in the Veterans Claims Domain Extension

Rejected because funding and payment models are not Veterans Claims
adjudication concepts and may apply across multiple EMF domains.

## References

- ADR-001: Domain Independence
- ADR-012: Domain Extension and Platform Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-027: Initial Production Intelligence Provider
- ADR-035: Core and Industry Extension Boundary

## Architectural Principle

EMF shall measure intelligence usage before it monetizes intelligence usage;
accounting preserves options, while billing remains a separate future policy.

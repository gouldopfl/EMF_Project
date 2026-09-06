# ADR-037: Deployment Profiles and Operational Simplicity

## Status

Accepted

## Context

EMF is designed to support users and organizations with very different
technical capabilities and deployment needs.

The Veterans Claims Domain Extension may be used by:

- individual veterans
- individual accredited representatives
- individual Veterans Service Officers
- solo accredited attorneys
- small professional practices
- Veterans Service Organizations
- nonprofit and sponsored programs
- state or institutional organizations
- larger enterprise deployments

The underlying EMF platform is becoming increasingly capable and configurable.

Deployment choices may eventually include local execution, dedicated
workstations or MiniPCs, virtual machines, cloud infrastructure, hosted
services, different persistence providers, different intelligence providers,
different security configurations, and different cost or sponsorship models.

Those capabilities are valuable to operators and organizations, but they
must not become prerequisites for ordinary users.

In particular, an individual veteran should not need to understand concepts
such as virtual machines, databases, encryption providers, AI deployments,
network topology, token accounting, or service composition in order to use
the Veterans Claims application safely.

The same principle applies, at an appropriate scale, to individual VSOs,
accredited representatives, and solo attorneys.

## Decision

EMF shall separate operational configuration complexity from ordinary
application use through deployment profiles, safe defaults, automatic
configuration, and progressive exposure of advanced settings.

A deployment profile shall describe an intended operating environment and
select compatible infrastructure and application defaults.

Users shall interact primarily with domain workflows and application
capabilities rather than infrastructure configuration.

Configuration that can be safely derived by the application shall not require
manual user selection.

Advanced configuration shall remain available to authorized operators when a
deployment requires it.

## Deployment Profiles

EMF may support profiles such as:

- Individual
- Local Professional
- Small Organization
- Sponsored Organization
- Enterprise
- Managed or Hosted

Profile names and exact packaging may evolve without changing this decision.

A deployment profile may determine defaults for concerns such as:

- persistence provider
- artifact storage
- intelligence provider
- cryptographic provider
- local versus remote services
- resource limits
- concurrency
- backup behavior
- update behavior
- logging and diagnostics
- usage accounting
- administrative interfaces

Profiles shall compose existing provider-neutral EMF boundaries rather than
introducing profile-specific domain behavior.

## Individual and Local Deployments

EMF shall remain capable of operating in a practical local deployment when
the selected capabilities can be supported by the available hardware.

A virtual machine shall not be an architectural requirement merely because
EMF may also be deployed using virtual machines or cloud infrastructure.

Local deployment may use a sufficiently capable workstation, MiniPC, or other
supported computer without requiring the ordinary user to administer a
separate server environment.

Local operation shall not weaken required integrity, authorization,
cryptographic, provenance, audit, or protected-information controls.

Where a local deployment cannot safely provide a required capability, the
application shall fail clearly or use an explicitly configured approved
service rather than silently weakening the control.

## Automatic Configuration

EMF should automatically determine configuration when the choice can be made
safely and deterministically.

Examples may include:

- selecting compatible local persistence
- initializing required schemas
- establishing application directories
- selecting resource limits appropriate to the deployment profile
- detecting available optional capabilities
- configuring safe local defaults
- validating provider availability
- validating required cryptographic services
- determining whether a feature should be enabled

Automatic configuration shall fail closed for security-sensitive decisions.

The application shall not silently substitute a less protective provider or
configuration because the preferred configuration is unavailable.

## Progressive Configuration

Configuration shall be exposed according to responsibility.

An ordinary user should generally see:

- the task being performed
- information required to complete that task
- meaningful status
- actionable errors
- choices that materially affect the user's work

An authorized administrator or operator may additionally see:

- provider configuration
- deployment diagnostics
- infrastructure settings
- resource controls
- security configuration
- accounting configuration
- organizational policy

Developer-level implementation details shall not become ordinary end-user
configuration merely because the platform supports them.

## Portability

Deployment profiles shall not change EMF domain semantics.

The same claim, evidence, artifact, provenance, workflow, or intelligence
concept shall retain its meaning whether EMF is running:

- on a local computer
- on a dedicated MiniPC
- inside a virtual machine
- in a cloud environment
- as part of a larger organizational deployment

Infrastructure affects how capabilities are provided, not what the domain
concepts mean.

## Security and Privacy

Operational simplicity shall not be achieved by disabling required controls.

Safe defaults shall favor:

- least privilege
- protected storage
- authenticated access where required
- encryption appropriate to the deployment
- explicit provider authorization
- bounded resource consumption
- auditable security-sensitive operations
- fail-closed behavior

A simplified user experience may hide implementation complexity but shall not
hide security failures that require action.

## Failure Behavior

When automatic configuration cannot establish a safe usable environment, EMF
shall provide an understandable diagnostic appropriate to the user's role.

The ordinary user should receive an actionable explanation rather than raw
provider, database, cryptographic, or infrastructure exceptions whenever the
application can safely translate them.

Detailed diagnostics may remain available to authorized operators and
developers.

## Verification Requirements

As deployment profiles and automatic configuration are implemented, automated
tests shall verify that:

- profiles select only compatible provider combinations
- security-sensitive configuration fails closed
- required controls are not disabled by simplified profiles
- domain behavior remains independent of deployment profile
- local and hosted configurations preserve the same domain contracts
- missing infrastructure produces controlled diagnostics
- automatic defaults are deterministic
- advanced settings are not required for ordinary workflows
- provider-specific types do not leak into domain configuration
- configuration failures do not corrupt persisted evidence or workflow state

## Consequences

### Benefits

- Individual veterans can use EMF without becoming system administrators.
- Solo attorneys and individual VSOs can use practical local deployments.
- Small organizations can operate EMF without enterprise-scale
  infrastructure.
- Larger organizations retain access to advanced configuration.
- EMF can support both local and hosted deployment models.
- Infrastructure complexity remains behind provider-neutral boundaries.
- Safe defaults reduce configuration errors.
- Future deployment options can be added without redesigning domain
  workflows.

### Tradeoffs

- Deployment-profile design and validation become platform responsibilities.
- Automatic configuration requires careful compatibility testing.
- Different deployment sizes may require different operational guidance.
- Some advanced capabilities may not be available in every profile.
- Good error translation requires additional implementation work.
- Safe defaults must evolve as providers and security requirements change.

## Rejected Alternatives

### Require users to configure every capability

Rejected because EMF contains infrastructure decisions that ordinary users
should neither need to understand nor be responsible for configuring.

### Require a virtual machine for every installation

Rejected because virtualization is a deployment mechanism, not an EMF domain
or platform requirement. Capable local hardware may provide a simpler and
less expensive deployment.

### Create separate products for every deployment size

Rejected because individual, professional, organizational, and enterprise
deployments should share the same platform and domain semantics.

### Hide all configuration

Rejected because administrators, organizations, and regulated deployments
require explicit control over infrastructure, security, providers, and policy.

### Weaken controls for simple local installations

Rejected because usability does not justify weakening integrity, security,
privacy, provenance, or audit requirements.

## References

- ADR-001: Domain Independence
- ADR-011: Presentation and Deployment Independence
- ADR-015: Persistence Provider Selection and Composition
- ADR-017: Protected and Regulated Information Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-035: Core and Industry Extension Boundary
- ADR-036: Deferred Billing and AI Usage Accounting

## Architectural Principle

EMF may be operationally sophisticated without requiring its users to become
infrastructure experts.

Complexity belongs behind safe boundaries; users should see the work they
need to accomplish, while EMF manages what it can safely manage underneath.

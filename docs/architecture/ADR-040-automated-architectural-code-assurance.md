# ADR-040: Automated Architectural and Code Assurance

## Status

Accepted

## Date

2026-09-07

## Context

EMF relies on explicit architectural, security, integrity, persistence, resource, workflow, extension, and intelligence boundaries.

Existing tests enforce selected rules, but hardening still requires repeated manual searches for classes of weakness such as unbounded parser inputs, unchecked returned identities, unsafe persistence assumptions, missing provenance validation, provider leakage, and authority violations.

As the repository grows, manual discovery alone will not scale reliably. EMF needs automated whole-repository assurance that can inspect source directly, understand solution structure when deeper context is required, identify likely boundary violations, and prioritize the next areas for engineering review.

Automated assurance complements rather than replaces tests, dependency auditing, fuzzing, runtime verification, penetration testing, and human review.

## Decision

EMF shall maintain a developer-focused architecture and code assurance tool under the repository `tools/` boundary.

Production framework projects shall not depend on the auditor or acquire Roslyn, MSBuild, analyzer, or remediation dependencies because of it unless a later architectural decision explicitly allows that dependency.

The auditor shall support two complementary analysis modes.

### Direct Source Analysis

The auditor may read `.cs` files directly and parse them into Roslyn syntax trees.

Direct-source rules may identify concerns such as:

- unbounded byte or text inputs before parsing or materialization
- unbounded collection, recursion, archive, document, image, or output processing
- suspicious direct file or stream reads
- missing cancellation checks at expensive boundaries
- broad exception handling or swallowed failures
- unsafe path or content handling
- missing validation around factory, repository, parser, or provider results
- provider-specific implementations referenced from restricted layers

### Solution-Aware Semantic Analysis

When correct analysis requires project, type, symbol, interface, implementation, or call-target information, the auditor shall load the solution or relevant projects and use Roslyn compilation and semantic models.

Semantic rules may enforce:

- allowed project dependency direction
- domain and extension boundaries
- persistence provider boundaries
- security and cryptographic adapter boundaries
- intelligence provider neutrality
- authority separation
- ownership and identity validation
- provenance and lineage requirements

A syntax-only result shall not be represented as semantic proof when compilation context is required.

## Versioned Assurance Rules

Every automated rule shall have a stable identifier, category, description, severity, and rule version.

Findings shall identify, where available:

- rule identifier and version
- severity
- project and source file
- symbol or syntax location
- line and column
- concise reason and relevant evidence
- suggested review or remediation direction
- syntax or semantic analysis mode

Material changes to rule meaning or detection behavior shall be observable through rule versioning.

## Prioritization

The auditor shall help determine where engineering attention should concentrate next.

Findings shall support deterministic prioritization using severity, confidence, affected boundary, and potential consequence.

Prioritization is advisory and does not authorize modification of source code or configuration.

## Outputs and Traceability

The auditor shall support machine-readable and human-readable results. Planned formats include JSON, Markdown, and SARIF.

Output shall identify the auditor version, executed rule versions, repository or source revision when available, execution time, and analysis mode sufficiently to reconstruct what was checked.

A clean report means only that the executed rules found no violations within their defined scope. It shall not be represented as proof that all software behavior is secure or correct.

## Remediation Authority

Automated remediation shall remain advisory unless a separately authorized mechanism explicitly applies it.

The auditor may generate proposed patches, diffs, or remediation artifacts. Generation and application are separate operations.

A remediation artifact should preserve the affected source identity or fingerprint, rule identity and version, proposed change, and reason.

Detection does not confer repository modification authority.

## Auditor Safety

The auditor is security-relevant developer tooling and shall protect its own boundaries.

It shall use configurable limits for source-file size, source-file count, aggregate input, generated output, and other expensive analysis where applicable.

Build, generated, package, and other excluded directories shall not be scanned unintentionally.

Malformed or unreadable input shall fail predictably rather than silently weakening assurance.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- representative violations are detected
- compliant code does not produce the same finding
- rule identifiers and versions remain stable and observable
- syntax rules do not claim semantic certainty
- semantic rules use the intended solution context
- excluded directories remain excluded
- resource limits protect the auditor
- malformed input fails predictably
- remediation generation does not modify source files
- reports identify their rules and analysis context
- production projects do not depend on the auditor

## Consequences

Benefits include earlier detection of architectural drift, repeatable hardening checks, faster discovery of likely security and integrity weaknesses, reduced repetitive searching, preserved lessons from prior hardening work, and better prioritization of engineering effort.

Costs include rule maintenance, analyzer dependencies, false-positive management, semantic-compilation complexity, and execution time.

## Rejected Alternatives

### Continue relying primarily on manual searches

Rejected because manual review remains valuable but does not scale as the repository and rule catalog grow.

### Adopt the prototype script unchanged

Rejected because syntax parsing is useful but insufficient for rules requiring project, symbol, type, or implementation context, and because the auditor itself needs explicit safety boundaries.

### Use regular expressions as the primary analysis engine

Rejected because textual searches are too fragile for authoritative architectural rules.

### Put Roslyn auditing into production framework assemblies

Rejected because compiler and repository-analysis dependencies belong to developer tooling.

### Automatically rewrite source when a violation is detected

Rejected because detection does not confer modification authority.

### Treat a clean static audit as proof of complete security

Rejected because static analysis cannot prove all runtime, dependency, concurrency, configuration, environmental, or adversarial properties.

## References

- ADR-001: Domain Independence
- ADR-012: Domain Extension Platform Boundary
- ADR-017: Protected and Regulated Information Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-028: Tamper-Evident Security Audit Storage
- ADR-031: Resource-Neutral Authorization
- ADR-038: AI Authority Separation
- ADR-039: AI Execution Traceability
- `tests/EMF.Tests/ArchitectureDependencyTests.cs`

## Architectural Principle

EMF shall continuously inspect its source and architecture against explicit, versioned assurance rules.

Automated analysis may inspect broadly and recommend where engineering attention should concentrate, while remediation remains a separate authorized operation.

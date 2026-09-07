# ADR-041: Developer Assurance Interface and Editor Integration

## Status

Accepted

## Date

2026-09-07

## Context

EMF now has automated architecture and code assurance under `tools/EMF.ArchitectureAuditor`, versioned assurance rules, focused tests, full regression testing, Git checkpoints and tags, architectural decision records, schema migrations, package inventories, and other repository evidence.

Developers currently invoke these capabilities through separate shell commands. That is workable for framework development, but it requires contributors to remember command syntax, understand which verification level is appropriate, and manually assemble project status information.

EMF is intended to support open-source contributors and developers with different levels of familiarity with the repository. Developer tooling should make the safe path obvious without duplicating architecture policy or testing logic in every editor integration.

Future editor integrations may include Visual Studio Code, Visual Studio, JetBrains Rider, and other development environments. These interfaces must not become independent implementations of assurance policy.

## Decision

EMF shall provide a developer-focused assurance interface under the repository `tools/` boundary.

The developer interface shall orchestrate existing build, test, audit, reporting, and repository-status capabilities. It shall not independently redefine architecture rules, security policy, test semantics, or remediation authority.

The initial implementation should be a command-line developer assurance tool. Editor integrations shall be thin presentation layers over the same underlying developer-assurance operations and structured results.

## Developer Operations

The developer assurance interface should expose, as applicable:

1. Perform Current Task
2. Validate Current File
3. Validate Current Project
4. Run Focused Tests
5. Run Project Tests
6. Run Full Regression
7. Run Architecture / Code Auditor
8. Run Complete Development Gate
9. Show Project Assurance Report
10. Generate Project Assurance Package
11. Show Findings / Next Work
12. Show Git / Checkpoint Status

The exact menu presentation may evolve without changing this decision.

### Perform Current Task

`Perform Current Task` shall be context-aware where practical.

It should determine the smallest useful verification scope for the developer's current work, such as building the owning project, running applicable focused tests, or running relevant assurance rules.

It shall not automatically substitute a full regression for every development action.

### Validate Current File

A source file shall not be treated as an isolated compilation unit when project context is required.

File validation should locate the owning project and use the real project or solution compilation context when semantic correctness depends on references, symbols, language settings, generated inputs, or build configuration.

### Verification Levels

Focused tests provide fast feedback while developing.

Project tests verify the affected project or bounded project set.

Full regression verifies the complete EMF solution.

The complete development gate may combine build, architecture/code assurance, focused or project validation, full regression, dependency checks, and other assurance mechanisms adopted later.

The interface shall distinguish a current PASS from a stale PASS. A previous successful regression must not be presented as current proof after relevant source, project, configuration, or dependency changes.

## Project Assurance Reporting

EMF shall provide a consolidated project assurance report assembled from repository evidence rather than manually maintained status text.

The report should include, where available:

- repository branch, revision, and working-tree state
- framework and language versions
- build status
- focused, project, and full-regression results
- test totals, passes, failures, skips, and elapsed time
- architecture and code assurance rule status, versions, parse errors, and findings
- ADR inventory and status
- database migration inventory and sequence
- project, package, and dependency inventories
- dependency and vulnerability audit status when available
- Git tags, checkpoints, and significant milestones
- verification freshness and the repository revision against which results were produced
- unresolved findings and recommended next work

The report shall identify the evidence revision it represents so that a later reader can distinguish historical assurance from current repository state.

JSON shall be the canonical structured assurance artifact.

Markdown, SARIF, DOCX, PDF, editor panels, dashboards, and other presentations should be generated from or remain traceably consistent with the same structured results rather than maintaining independent conclusions.

Formal reports shall use professional, deterministic language. Interactive developer interfaces may include clearly non-authoritative personality or humor without altering assurance results.

## Editor Integration

The first implementation may be a command-line developer interface.

Future integrations may include Visual Studio Code, Visual Studio, JetBrains Rider, and other development environments.

Editor integrations shall:

- invoke the common developer-assurance capabilities
- present the same rule identifiers, versions, severity, confidence, and status
- navigate to affected files, lines, or symbols when available
- preserve verification freshness
- avoid embedding independent copies of architecture or testing policy
- avoid treating editor state as the authoritative assurance record

A future editor panel may expose actions such as `Perform Current Task`, `Run Focused Tests`, `Run Full Regression`, `Run Complete Development Gate`, and `Generate Project Assurance Package`.

Developer-facing presentation may include clearly non-normative humor or personality, such as reporting how much of a developer's retirement a full regression consumed. Formal JSON, SARIF, Markdown, DOCX, PDF, audit, and assurance artifacts shall present the corresponding measurements professionally and shall not rely on such presentation text.


## Structured Results

Developer operations should produce structured results suitable for both command-line and editor consumers.

Where applicable, results should include:

- operation identity
- repository revision
- worktree state
- start and completion time
- duration
- success, failure, skipped, unavailable, or stale status
- test counts
- rule identities and versions
- findings and source locations
- generated artifact locations
- diagnostics needed to understand failure

Presentation layers shall not infer PASS when the underlying operation did not produce sufficient evidence.

## Remediation and Authority

Developer convenience does not change EMF authority boundaries.

The interface may execute explicitly selected development operations and may display or generate remediation proposals, but detection and reporting do not confer authority to modify production evidence, protected data, operational state, or repository source without the separately authorized action required for that operation.

Editor integration shall not silently apply remediation merely because a finding is displayed.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- developer operations invoke the intended underlying capability
- focused, project, and full-regression operations remain distinct
- complete development gates fail when required checks fail
- skipped or unavailable required checks are not reported as PASS
- stale results are not represented as current verification
- current-file validation uses owning project context when required
- project reports identify the repository revision and executed checks
- editor-facing results preserve rule identities and source locations
- production projects do not depend on developer tooling
- editor adapters do not duplicate authoritative assurance rules

## Consequences

Benefits include a consistent contributor workflow, reduced command memorization, faster feedback, clearer verification freshness, reusable editor integration, improved onboarding, and reproducible project reporting.

Costs include maintaining developer orchestration, structured result contracts, verification freshness, report generation, and editor-specific presentation adapters.

## Rejected Alternatives

### Put developer tooling into `EMF.Console`

Rejected because `EMF.Console` is a production composition root and developer tooling should remain outside production runtime boundaries.

### Implement assurance separately in each editor

Rejected because duplicated policy would drift and produce inconsistent conclusions.

### Treat the latest successful test run as current indefinitely

Rejected because later source, dependency, configuration, or worktree changes can make earlier verification stale.

### Run full regression automatically after every development action

Rejected because focused feedback is materially faster and the full regression should run when explicitly selected or required by a development gate.

### Maintain project reports manually

Rejected because project status should be reproducible from repository evidence and structured assurance results.

## References

- `docs/DECISIONS.md` ADR-0003: EMF Tests Its Own Principles
- ADR-038: AI Authority Separation
- ADR-039: AI Execution Traceability
- ADR-040: Automated Architectural and Code Assurance

## Architectural Principle

EMF developer interfaces shall provide one consistent path to build, test, audit, report, and understand the current development state while keeping assurance logic centralized, traceable, and independent of any specific editor.

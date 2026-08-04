# Architectural Decisions

## ADR-0001 — Domain Independence

**Status:** Accepted

### Decision

The Evidence Management Framework core shall remain independent of any single industry, institution, data source, regulation, or evidence domain.

Domain-specific terminology, rules, interpretation, and processing shall be implemented in domain models and adapters.

### Rationale

EMF is intended to serve many industries. Coupling the core to its first implementation would reduce reuse and make future maintenance harder.

### Consequences

- Core documentation and code use domain-neutral terminology.
- The Veteran Model contains veteran-, claims-, and healthcare-specific behavior.
- Domain models may evolve independently of the core.

---

## ADR-0002 — The Universality Test

**Status:** Accepted

### Decision

Every proposed capability shall be evaluated by asking:

> Can this concept be used unchanged across multiple domains, or does it depend on domain knowledge?

A universally reusable concept belongs in the EMF core. A concept requiring specialized terminology, interpretation, regulation, or practice belongs in a domain model or adapter.

### Rationale

Most future development is expected to occur in domain models. Maintaining a clear boundary keeps the core stable and makes the framework easier for future maintainers to understand.

### Consequences

- Evidence, provenance, lifecycle, validation, inventory, and processing contracts may belong in the core.
- OSCAR event meanings, VA claims concepts, medical interpretations, and veteran-specific reporting belong in the Veteran Model.
- Domain complexity shall not leak into the core.

---

## ADR-0003 — EMF Tests Its Own Principles

**Status:** Accepted

### Decision

EMF shall apply its evidence-management principles to its own development whenever practical.

Documentation, source code, tests, configuration, and schemas are treated as artifacts with traceable creation, update, rename, and deletion histories.

### Rationale

The EMF repository is the framework’s first practical evidence source and provides a continuous test of provenance, lifecycle, and traceability concepts.

### Consequences

- Completed documentation and implementation tasks are committed promptly.# Architectural Decisions

## ADR-0001 — Domain Independence

**Status:** Accepted

### Decision

The Evidence Management Framework core shall remain independent of any single industry, institution, data source, regulation, or evidence domain.

Domain-specific terminology, rules, interpretation, and processing shall be implemented in domain models and adapters.

### Rationale

EMF is intended to serve many industries. Coupling the core to its first implementation would reduce reuse and make future maintenance harder.

### Consequences

- Core documentation and code use domain-neutral terminology.
- The Veteran Model contains veteran-, claims-, and healthcare-specific behavior.
- Domain models may evolve independently of the core.

---

## ADR-0002 — The Universality Test

**Status:** Accepted

### Decision

Every proposed capability shall be evaluated by asking:

> Can this concept be used unchanged across multiple domains, or does it depend on domain knowledge?

A universally reusable concept belongs in the EMF core. A concept requiring specialized terminology, interpretation, regulation, or practice belongs in a domain model or adapter.

### Rationale

Most future development is expected to occur in domain models. Maintaining a clear boundary keeps the core stable and makes the framework easier for future maintainers to understand.

### Consequences

- Evidence, provenance, lifecycle, validation, inventory, and processing contracts may belong in the core.
- OSCAR event meanings, VA claims concepts, medical interpretations, and veteran-specific reporting belong in the Veteran Model.
- Domain complexity shall not leak into the core.

---

## ADR-0003 — EMF Tests Its Own Principles

**Status:** Accepted

### Decision

EMF shall apply its evidence-management principles to its own development whenever practical.

Documentation, source code, tests, configuration, and schemas are treated as artifacts with traceable creation, update, rename, and deletion histories.

### Rationale

The EMF repository is the framework’s first practical evidence source and provides a continuous test of provenance, lifecycle, and traceability concepts.

### Consequences

- Completed documentation and implementation tasks are committed promptly.
- Git history serves as the authoritative lifecycle record for repository artifacts.
- Future Git adapters may allow EMF to examine its own history as evidence.
- Git history serves as the authoritative lifecycle record for repository artifacts.
- Future Git adapters may allow EMF to examine its own history as evidence.

# ADR-044: Reviewed Medical Literature Promotion Lifecycle

## Status

Accepted

## Date

2026-09-12

## Context

The Veterans Claims Domain Extension can use intelligence capabilities to
classify medical or scientific literature against regulatory Requirements and
to identify relevant source excerpts.

Those classifications are derived intelligence until an authorized human
reviews and promotes them. Once promoted, they can influence reviewer-package
content and physician-facing explanation of why literature was selected.

The promoted record therefore requires stronger lifecycle semantics than a
transient classification proposal. EMF must preserve the human decision,
source lineage, intelligence provenance, and exact accepted excerpts while
preventing duplicate, partial, stale, or ambiguous active reviews.

ADR-038 separates intelligence capability from operational authority.
ADR-039 requires traceability when intelligence becomes an authorized result.
ADR-042 requires printable source evidence to preserve source identity and
traceability. This ADR applies those principles specifically to reviewed
medical-literature classifications in the Veterans Claims Domain Extension.

## Decision

A medical-literature classification produced by intelligence shall remain a
proposal until explicitly promoted by an authorized human reviewer.

Promotion shall create an immutable reviewed classification that preserves:

- the regulatory Requirement
- the medical-literature source
- the guidance role
- the exact source Artifact
- the accepted relevance description
- the promoting and reviewing identities
- promotion and review timestamps
- the originating intelligence output and normalized execution provenance
- the correlation identifier
- provider, capability, engine, and operation identifiers when available
- execution start and completion times
- review requirement and warnings
- the exact accepted source excerpts and their ranges when ranges are present

The intelligence output is provenance for the reviewed decision. It is not
itself source evidence and shall not be presented as physician-facing evidence.

## Human Authority

Promotion and supersession are consequential actions and require an explicit
human reviewer identity.

The human-reviewed role, relevance description, and accepted excerpts are the
authoritative reviewed interpretation exposed to reviewer workflows.

AI output shall not silently promote itself, replace an accepted human review,
or acquire authority merely because a classification operation succeeded.

## Promotion Integrity

Reviewed promotion shall fail closed when required identity, timestamp,
provenance, association, Artifact lineage, or excerpt information is invalid or
internally inconsistent.

When one classification operation produces multiple reviewed decisions, their
promotion shall be atomic. Either the entire reviewed batch is persisted or no
new decision from that batch is persisted.

A logical reviewed decision is identified by:

- Requirement
- medical-literature source
- guidance role
- Artifact

Only one active accepted review may exist for a logical reviewed decision.
This invariant shall be enforced both by application validation and by the
persistence schema.

## Immutability and Supersession

An accepted reviewed classification shall not be updated in place.

A later human review that replaces an earlier accepted review shall use an
explicit supersession operation. Supersession shall:

- identify the prior accepted review by correlation identity
- create a new immutable reviewed classification
- preserve the superseded classification and its excerpts for audit history
- record the relationship between the prior and replacement reviews
- require the replacement batch to match the logical decision set being
  superseded
- complete transactionally so a failed replacement does not leave a partial
  active state

Normal active-review queries shall exclude superseded classifications.
Historical or audit-oriented retrieval may expose superseded records and their
lineage explicitly.

## Reviewer Package Behavior

Reviewer-package preparation shall use the Artifact lineage from the active
human-reviewed classification when such a classification exists.

Physician-facing reviewer material may display the active reviewed:

- Requirement
- guidance role
- relevance description
- reviewer identity and review time
- accepted source excerpts

The reviewer package shall not display superseded reviewed interpretations as
current decisions.

Raw intelligence output, superseded or current, shall not be presented as
physician-facing source evidence. Complete source material remains separately
preserved according to the reviewer-package and printable-evidence contracts.

When no reviewed classification exists, existing non-reviewed literature
fallback behavior may remain available where explicitly supported by the
workflow, but it shall not be misrepresented as a human-reviewed
classification.

## Concurrency and Failure Semantics

Persistence constraints and transactional checks shall prevent concurrent or
alternate writers from creating multiple active accepted reviews for the same
logical decision.

A uniqueness conflict, stale supersession target, mismatched replacement set,
invalid provenance, or other persistence failure shall fail the operation
without partially promoting the new reviewed batch.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- reviewed classifications preserve source Artifact and intelligence lineage
- malformed promotion metadata or excerpt ranges are rejected
- duplicate active reviewed decisions are rejected
- logical uniqueness is enforced by SQLite as well as repository validation
- multi-classification promotion is atomic
- explicit supersession preserves the prior review for audit history
- supersession exposes only the replacement review through active-review reads
- invalid or partial supersession rolls back
- reviewer-package source selection follows active reviewed Artifact lineage
- physician-facing DOCX output shows the active human-reviewed interpretation
- superseded interpretations do not appear as current reviewer guidance
- raw intelligence output does not leak into physician-facing evidence text

## Consequences

Benefits include unambiguous human authority, immutable review history,
transactional promotion, database-enforced uniqueness, auditable re-review,
precise literature Artifact lineage, and safer physician-facing reviewer
packages.

Costs include additional persistence metadata, explicit supersession
operations, stricter promotion validation, and more lifecycle tests.

The design intentionally favors explicit review history over convenient
in-place editing.

## Rejected Alternatives

### Update accepted classifications in place

Rejected because it destroys the historical human decision and weakens audit
reconstruction.

### Allow multiple active accepted reviews distinguished only by correlation ID

Rejected because reviewer-package consumers would have no authoritative basis
for choosing among competing human-approved interpretations.

### Delete the prior review when a replacement is accepted

Rejected because supersession is part of the evidentiary and review history and
must remain reconstructable.

### Persist each classification independently during one promotion operation

Rejected because a later failure could leave a partially promoted human review.

### Show raw AI classification output in the physician-facing package

Rejected because AI output is derived provenance, not source evidence or a
substitute for the human-reviewed interpretation.

## References

- ADR-013: Veterans Claims Domain Model Boundary
- ADR-026: Intelligence Services and Agent Boundary
- ADR-038: AI Authority Separation
- ADR-039: AI Execution Traceability
- ADR-042: Printable Source Evidence Preservation

## Architectural Principle

Reviewed medical-literature decisions are human-authorized, immutable, and
traceable.

Replacement occurs through explicit supersession, not silent mutation, and
only the active human-reviewed interpretation may drive current reviewer
presentation.

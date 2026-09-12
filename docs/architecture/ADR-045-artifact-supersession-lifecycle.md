# ADR-045: Artifact Supersession Lifecycle

## Status

Accepted

## Date

2026-09-12

## Context

Evidence is often corrected, completed, signed, or otherwise replaced after it
has already been ingested. The original bytes remain part of the evidentiary
history, but current workflows need an unambiguous way to identify the active
replacement.

Examples include a signed statement replacing an unsigned copy, a corrected
record replacing an earlier export, or a later document version replacing a
prior version without destroying provenance.

EMF already defines the generic `Supersedes` Artifact relationship, but the
relationship did not yet have an operational lifecycle, validation rules, or
active-version resolution semantics.

Deleting or overwriting the older Artifact would weaken provenance and make it
impossible to reconstruct what evidence was previously available. Treating
both versions as simultaneously current would create duplicate or conflicting
reviewer evidence.

## Decision

Artifact replacement shall be represented by an explicit directed
`Supersedes` relationship:

- the replacement Artifact is the relationship source
- the Artifact being replaced is the relationship target

Artifacts remain immutable evidence records. Supersession changes which
Artifact is considered active; it does not mutate or delete the superseded
Artifact.

A supersession chain therefore reads from newest to oldest through outgoing
relationships, for example:

`V3 -> Supersedes -> V2 -> Supersedes -> V1`

Active-version resolution starts from any Artifact in the chain and follows
incoming `Supersedes` relationships until no newer replacement exists.

## Supersession Integrity

Creating a supersession relationship shall fail closed when:

- either Artifact does not exist
- a repository returns an Artifact with the wrong identity
- an Artifact attempts to supersede itself
- the replacement already supersedes a different immediate predecessor
- the Artifact being replaced already has a different replacement
- a new relationship would use a replacement that has itself already been
  superseded
- repository relationship lookup returns unrelated records

Repeating the exact same supersession operation is idempotent and returns the
existing relationship.

An already recorded historical relationship remains idempotently observable
even after its replacement is later superseded by a newer Artifact.

Active-version resolution shall fail closed on ambiguous replacement branches,
missing replacement Artifacts, unrelated relationship results, or cycles.

## Current Evidence Selection

Domain workflows may resolve an Artifact reference to its active replacement
when the underlying logical evidence remains the same.

The Veterans Claims reviewer workflow shall resolve classified evidence through
Artifact supersession before extracting reviewer text. A classification on an
older Artifact therefore continues to identify the logical evidence lineage,
while the reviewer receives the newest active Artifact.

The classification itself is not rewritten or silently duplicated. The prior
Artifact and its classification remain part of history.

A reviewer package that was previously generated remains a historical record
of the evidence selected at that time. A newly generated package resolves the
current active Artifact.

## Human-Reviewed Derived Decisions

Artifact supersession does not automatically transfer a human-reviewed derived
decision onto changed content.

In particular, reviewed medical-literature classifications retain their own
explicit promotion and supersession lifecycle under ADR-044. Replacing a
medical-literature Artifact does not silently authorize the replacement under
the prior human review.

This distinction preserves the difference between replacing source evidence
and replacing an interpretation or human-approved decision about that source.

## Traceability

Both the current and superseded Artifacts remain retrievable through the
evidence repository.

Reviewer-package traceability may expose the `Supersedes` relationship for the
active Artifact so that the replacement lineage can be reconstructed without
showing the obsolete Artifact as current evidence.

Supersession timestamps are preserved on the relationship record.

## Console Workflow

The Veterans Claims console shall expose an explicit command for recording an
Artifact replacement after both Artifacts have been ingested:

`emf veterans evidence supersede <database-path> <replacement-artifact-id> <superseded-artifact-id>`

The command is intentionally explicit. EMF shall not infer supersession merely
from similar filenames, timestamps, or file contents.

## Verification Requirements

Automated tests shall verify, where applicable, that:

- supersession persists the correct directed relationship
- exact replay is idempotent
- self-supersession is rejected
- conflicting replacements are rejected
- obsolete Artifacts cannot be introduced as new replacements
- valid multi-version chains resolve to the newest Artifact
- ambiguous branches and cycles fail closed
- reviewer classified evidence resolves to the active replacement
- the original classification remains effective for the active evidence
  lineage
- the console command persists and reuses the supersession relationship

## Consequences

Benefits include immutable evidence history, explicit replacement semantics,
unambiguous current-version selection, safer reviewer packages, and auditable
correction/signature workflows.

Costs include additional relationship validation and the need for callers to
record replacement intentionally after ingesting a new version.

## Rejected Alternatives

### Overwrite the original Artifact content

Rejected because the original evidence and its fingerprint would be lost.

### Delete the old Artifact after replacement

Rejected because historical reviewer packages, classifications, provenance,
and audit reconstruction may depend on it.

### Infer replacement from filename similarity

Rejected because similarly named files are not sufficient evidence of intent
to supersede.

### Copy every classification to the replacement Artifact

Rejected because it creates duplicated mutable state. Current classified
reviewer evidence can resolve through the immutable supersession lineage
instead.

### Automatically transfer reviewed medical-literature decisions

Rejected because a human-reviewed interpretation applies to the exact reviewed
Artifact and requires its own explicit supersession lifecycle.

## References

- Evidence Storage Model
- ADR-013: Veterans Claims Domain Model Boundary
- ADR-039: AI Execution Traceability
- ADR-042: Printable Source Evidence Preservation
- ADR-044: Reviewed Medical Literature Promotion Lifecycle

## Architectural Principle

Evidence versions are immutable and replacement is explicit.

Current workflows may follow validated supersession lineage to the active
Artifact, while prior versions remain preserved and reconstructable.

# ADR-042: Printable Source Evidence Preservation

## Status

Accepted

## Date

2026-09-08

## Context

EMF preserves source artifacts as evidence and may derive text, classifications,
summaries, intelligence, and other representations from those artifacts.

Extracted text is valuable for search, analysis, accessibility, classification,
and intelligence, but it is not a substitute for the source document. Text
extraction can lose layout, handwriting, signatures, stamps, tables, images,
annotations, spacing, pagination, and other visually significant evidence.

Evidence packages may be reviewed, exported, printed, transmitted, or retained
outside the system that originally ingested the evidence. A package that
contains only reconstructed text can therefore fail to preserve material
information present in the submitted source artifact.

This requirement is independent of Veterans Claims. Any evidence domain may
need a reviewer to see the source document in substantially the same visual
form in which it was supplied.

## Decision

EMF shall preserve the original artifact content as the authoritative source
representation.

When the original artifact is printed, exported, or otherwise supplied as the
original source, EMF shall use the authoritative preserved artifact content
directly. A rendered package representation shall not be substituted for the
original artifact.

When a source artifact is included as underlying evidence in a package, EMF
shall also provide a faithful printable representation derived from the
preserved source artifact.

Extracted text, summaries, OCR output, classifications, intelligence results,
and other derived representations shall not replace the printable source
representation.

Printable representations shall retain traceability to the authoritative
artifact, including artifact identity and integrity information.

Package generation shall fail closed when a required underlying source artifact
cannot be rendered into an acceptable printable representation. EMF shall not
silently substitute extracted text for an unavailable source rendering.

Generated organizational material is not an uploaded source artifact and may
be rendered directly in the package output format.

## Architectural Boundary

The capability to obtain a printable representation of an artifact is
industry-independent and belongs behind EMF Core contracts and universal
Orchestration services.

Format-specific rendering implementations may depend on libraries appropriate
to their document formats, but those implementation details shall not escape
the rendering boundary.

Industry extensions and package adapters may:

- identify artifacts as underlying evidence
- organize evidence into domain-specific sections or appendixes
- select package output formats
- add domain-specific headings and explanatory material

Industry extensions shall not redefine preservation semantics or treat
extracted text as a replacement for the authoritative source artifact.

## Rendering Requirements

A printable source representation shall:

- originate from preserved artifact content rather than an external source path
- maintain association with the source Artifact ID
- preserve the meaningful visual content of the source
- observe bounded input, page, dimension, memory, and output limits
- honor cancellation where applicable
- reject malformed, unsupported, or unsafe content rather than producing a
  misleading representation
- remain a derived representation; it shall not replace or modify the
  authoritative stored artifact

For paged documents, package output should preserve page order.

For image evidence, the image itself is the printable representation when its
format can be safely embedded or rendered.

Additional document formats may be supported incrementally through
format-specific renderers.

## Package Requirements

An evidence package containing underlying source evidence shall not be
considered complete merely because text extraction succeeded.

Package preparation and export shall distinguish:

1. authoritative source artifact content
2. printable source representation
3. extracted or derived text
4. generated organizational material

Package presentation may include extracted text for accessibility or analysis,
but the printable source evidence must remain available to the reviewer.

## Consequences

### Positive

- Preserves visual evidence that OCR or text extraction may lose.
- Makes printed reviewer packages materially faithful to submitted evidence.
- Maintains a clear distinction between original and derived evidence.
- Supports future evidence domains without Veterans-specific coupling.
- Enables package validation to detect missing renderable evidence.
- Strengthens evidentiary traceability and reviewer confidence.

### Negative

- Package generation requires additional rendering infrastructure.
- Exported packages may become substantially larger.
- Each supported source format requires a safe rendering strategy.
- Rendering must be bounded against hostile or malformed content.
- Some formats may initially be unsupported and therefore prevent package
  completion until an appropriate renderer exists.

## Rejected Alternatives

### Use extracted text as the package copy of the source document

Rejected because extraction can discard visually significant evidence and does
not reproduce the document that was supplied.

### Preserve only the original file and omit it from printable packages

Rejected because reviewers of printed or consolidated package output must be
able to inspect the underlying evidence without requiring a separate evidence
system.

### Implement source rendering only in Veterans Claims

Rejected because faithful printable preservation is a universal evidence
management capability rather than a Veterans Affairs rule.

### Allow package generation to continue when rendering fails

Rejected because silently omitting or replacing source evidence can create an
apparently complete but materially incomplete evidence package.

## References

- ADR-001: Domain Independence
- ADR-002: The Universality Test
- ADR-021: Artifact Content Protection Boundary
- ADR-035: Core and Industry Extension Boundary

## Architectural Principle

Evidence packages may organize and explain source evidence, but they shall not
erase the distinction between the evidence that was supplied and the
representations derived from it.

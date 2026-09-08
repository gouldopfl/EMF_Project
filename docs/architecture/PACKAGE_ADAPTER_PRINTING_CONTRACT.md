# Package Adapter Printing Contract

## Purpose

This contract applies ADR-042, Printable Source Evidence Preservation, to evidence package adapters and exporters.

A package adapter may organize evidence for a domain, but it must not replace an uploaded source document with extracted text or another derived representation.

## Underlying Evidence

When an artifact is assigned the `UnderlyingEvidence` content role:

- the preserved artifact content is the authoritative source
- the package must include a faithful printable representation derived from that preserved content
- extracted text may also be included for search, accessibility, classification, intelligence, or explanation
- extracted text does not satisfy the printable-source requirement by itself
- artifact identity, fingerprint, provenance, and package role must remain traceable to the printable representation
- page order must be preserved for paged source documents

If a required underlying evidence artifact cannot be rendered faithfully and safely, package export must fail closed. The adapter must not silently omit the source or substitute extracted text.

## Generated Organizational Material

Artifacts assigned the `GeneratedOrganizationalMaterial` content role are
generated package material rather than uploaded source evidence.

They may be rendered directly in the selected package output format and do not
require a separate original-source rendering.

Generated organizational material must remain distinguishable from underlying
evidence.

## Rendering Boundary

Printable rendering is an EMF platform capability.

Core contracts define the rendering boundary. Universal orchestration services
and format-specific renderers implement it. Domain adapters consume those
capabilities without taking ownership of preservation semantics.

Format-specific implementations must observe bounded input, page, dimension,
memory, and output limits and must reject malformed or unsafe content rather
than produce a misleading representation.

## Package Completeness

A package containing underlying evidence is complete only when every required underlying artifact has:

1. authoritative preserved content
2. a faithful printable representation
3. retained artifact traceability

Derived text is additional package material and does not replace any of these requirements.

## Veterans Claims

The Veterans Claims extension may organize underlying evidence into claim-specific sections and appendixes, including service records, medical evidence, lay evidence, and adjudicative records.

For example, a DD214 included as service-record evidence must appear in the reviewer package as a faithful rendering of the preserved DD214, while extracted text may separately support classification, intelligence, and accessibility.

## References

- ADR-042: Printable Source Evidence Preservation
- ADR-035: Core and Industry Extension Boundary
- ADR-021: Artifact Content Protection Boundary

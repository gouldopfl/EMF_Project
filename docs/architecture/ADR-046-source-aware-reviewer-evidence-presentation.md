# ADR-046: Source-aware reviewer evidence presentation

## Status

Proposed

## Date

2026-09-23

## Context

Physician packages use a shared DOCX renderer; PDF output converts that same
DOCX. The renderer previously applied a single Blue Button header expression
without checking source identity, merged extracted rows as narrative, and
omitted historical medication sections under the existing physician-facing policy.
Header variants survived and row/status relationships could be obscured. These are presentation problems across claim
types, not condition-specific evidence-selection problems.

## Decision

Place Blue Button text normalization in the ephemeral
`VeteransReviewerEvidencePresentation` boundary consumed by the shared reviewer
renderer for both text content and printable text pages. The boundary receives
source context, never claim names, diagnoses, theories, or basis identifiers.

Recognize Blue Button through the resolved source name (or artifact name when
no parent source is available), not a mention inside extracted text. Apply source rules independently of appendix classification, including Blue Button
secure messages classified as lay evidence. Text mentions alone do not qualify.

Remove complete recognized producer headers and footers, standalone source-page
counters, and scrambled producer fragments in a bounded, ordered generation/DOB/
page-and-identity cluster. DOB dates may precede clinical data on the same line.
For verified Blue Button sources, also remove the exact standalone My HealtheVet
generation line by itself; inline mentions and all non-Blue-Button occurrences
remain text.
Header identities may split between any name tokens and end with extraction
punctuation. Match only the exact name established by a complete producer header,
or, when no such header is available, the known veteran display name supplied by
the renderer. A verified source identity takes precedence over a shorter display
label. Never extend an identity by guessing that subsequent capitalized lines
are more name tokens. Unknown names remain visible; no status-word blacklist
is used.
Only the matched producer spans are removed; intervening text is preserved.
Ambiguous clusters with multiple DOB markers remain intact. Remove
exact repeated identity lines learned from a complete header in that artifact. Keep unrecognized or ambiguous identity fragments. Do not remove
standalone status words such as ACTIVE: they may encode medication or disease
status. Preserve clinical text, dates, doses, measurements, order, line boundaries
and horizontal table spacing. Use monospaced presentation for tab-delimited rows or rows with multiple
inter-column spacing runs rather than guessing missing column relationships or merging them into
narrative. Preserve the existing physician-facing historical-medication omission policy,
including its explanatory message and references. Current medication and
basis-scoped medication presentation remain unchanged.

This view is local to rendering. It does not update stored artifacts, extracted
text, printable page buffers, provenance, classifications, or reviewed literature.
Source corrections are still validated against unmodified source text before
presentation normalization. Existing basis selection and reviewed-literature
rules remain upstream. Veteran-name title pages remain unchanged.

Faithful PDF/DOCX source images continue through the platform print-rendering
path unchanged. Normalized text does not replace those images, bypass a rendering
failure, or satisfy an otherwise missing printable-source requirement. Original
exports continue to use preserved content under ADR-042. All existing content,
identity, page order, encoding, and provenance validation remains in force.

## Deterministic presentation and pagination (Phase 2)

Rendering is a deterministic transformation of the supplied reviewer details.
It has no AI/provider dependency. EMF constructs the canonical DOCX; LibreOffice
performs final pagination and PDF conversion. Neither source page counts nor
estimated text lengths determine reviewer continuation titles.

Each evidence item occupies its own DOCX section, starting on a new page. The
body contains its human-readable title. A first-page header contains only the
complete package running header; the section's default header retains that full
header and adds the evidence title followed by “— Continued”. Explicit first/default references prevent
header inheritance between artifacts. Both reference the existing package footer,
including PAGE and NUMPAGES, without restarting numbering. Appendix cover
sections reset the evidence header. This replaces repeating title-table rows
and source-page-based continuation counters for every appendix and representation.

V5 producer cleanup remains an independent source-aware stage. Text layout then
distinguishes narrative, fields, preformatted/form material, and structural
boundaries. Only unfinished prose followed by a lowercase continuation is joined;
indents, measurements, codes, explicit whitespace, headings, statuses and fields
prevent speculative joining. Tables/forms retain spacing and order with a
smaller monospaced font. No missing cells or column relationships are invented.

Authoritative printable source pages take precedence over extracted publication
text, including cached MedicalLiteratureReviewerText. That property contains
publication extraction, not the package's reviewed summary. Reviewed relevance,
provenance and basis filtering remain in their existing services and opening
literature section. Supplied pages still pass ordering, encoding, format and
image validation; a failed page cannot be bypassed with reviewer text. Text
fallback applies only when the renderer receives no printable representation;
it does not relax ADR-042 preparation/printing requirements.

Reviewer typographical corrections use a small, case-sensitive dictionary of
lowercase whole-word rules. They apply only to eligible extracted narrative.
Fields, forms, protected medication/diagnosis sections, quotations, clinical
identifiers, values and uncertain contexts are left unchanged. Capitalized
names and uppercase abbreviations are not corrected.
An otherwise structural boundary receives a correction only when its complete
trimmed content is one exact lowercase dictionary misspelling token; arbitrary
headings, statuses and list lines remain unchanged.
Scientific publication text and native source images are never spell-corrected.
Each applied edit carries a rule identifier, original/replacement strings,
reviewer block locator and character offset, and the artifact displays a concise
correction notice. The rules are idempotent and do not write to evidence storage.
This intentionally limited facility is not a general spellchecker.

Opening reviewer subsections use one shared paragraph-spacing helper with 240
twips of space before a subsection, rather than inserted empty paragraphs.

Section/header mechanism references:
- [OpenXML TitlePage](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.wordprocessing.titlepage)
- [LibreOffice page headers](https://help.libreoffice.org/latest/en-GB/text/swriter/01/page.html)

## Consequences and limits

- Primary, secondary, and combined secondary bases share the same presentation
  behavior. No intelligence provider is needed for normalization.
- Clinical status fragments remain visible on separate lines when the original
  extraction does not establish their row association. Inventing an association
  or deleting the value would risk changing meaning.
- Unknown header layouts and ambiguous identity text remain visible. Extending
  recognition requires a representative fixture and content-preservation tests.
- Raster source pages retain all source headers. Cleaning their pixels or
  replacing them with extracted text would conflict with faithful evidence
  preservation; this decision does neither.
- Evidence and appendix sections start on new pages. This can increase page
  count, but avoids page-break estimation and continuation identity leakage.
- Synthetic DOCX-to-PDF regression tests exercise installed LibreOffice with
  EMF_REVIEWER_LAYOUT_TESTS=true. Other layout engines/versions can paginate
  differently; no fixed reviewer page count is promised.
- Typographical correction coverage is deliberately narrow; unmatched and
  ambiguous spellings remain visible for human review.
- Monospaced rows preserve available text layout but cannot reconstruct column
  positions already lost during extraction. Very wide rows may wrap in Word.

## Validation

Regression coverage includes byte-for-text preservation around interleaved clinical
and message producer fragments, grounded split identities followed by arbitrary
clinical/status lines, Blue Button headers and inline headers, repeated
identities, medical dates/measurements/status, historical medications, ordinary
PDF/DOCX printable images, lay evidence, literature, multi-page evidence,
canonical DOCX-to-PDF handoff, title-page veteran names, invalid UTF-8, and actual
simple/multi-basis secondary literature selection followed by rendering.
Phase 2 adds OpenXML schema validation, actual LibreOffice pagination for two
consecutive text artifacts and two consecutive native-page artifacts, synthetic
multi-column journal tables/figures spanning pages, plain/XML table fallback,
reviewer subsection spacing, and deterministic correction/non-correction cases.

## References

- ADR-042: Printable Source Evidence Preservation
- Package Adapter Printing Contract
- ADR-044: Reviewed Medical Literature Promotion Lifecycle

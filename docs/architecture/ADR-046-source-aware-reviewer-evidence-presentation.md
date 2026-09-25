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
Producer cleanup consumes complete scrambled signatures before stripping isolated
generation lines, so a standalone generation marker remains available to identify
its bounded cluster. A contiguous generation/page-counter footer followed by an
exact grounded identity also removes that identity (including split name tokens);
unknown names, longer names and names followed by prose remain visible. No name
or DOB is removed globally based only on the display label.
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
path. Literature may receive the lossless whole-page orientation and strictly
white border removal described below; its original content remains intact.
Normalized text does not replace those images, bypass a rendering
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
Fragmented runs require at least four consecutive isolated tokens in one bounded
region. Preformatted treatment starts at those tokens, never at preceding headings
or fields. Blank lines, prose, empty-value labels, dividers, explicit heading markup,
numbered section titles, and multiword structural lines stop the region regardless
of heading capitalization.
Token counts do not accumulate across fields or boundaries. Explicitly spaced/tabbed
rows retain their independent preformatted classification. Ambiguous short groups
retain their original classification; no semantic relationship is inferred across
a blank line merely to keep fonts uniform. Original lines, order and indentation
survive even when extraction has lost the original column coordinates. The renderer
retains this block classification instead of reclassifying each string. These runs
do not become label/value tables, merge as lists, or form keep-with-next chains.
Preformatted rows have no extra paragraph-after spacing. Source blank lines remain
one blank row, without the additional after-spacing of ordinary prose paragraphs.
This improves faithful fallback layout; it does not recover lost source geometry.

Authoritative printable source pages take precedence over extracted publication
text, including cached MedicalLiteratureReviewerText. That property contains
publication extraction, not the package's reviewed summary. Reviewed relevance,
provenance and basis filtering remain in their existing services and opening
literature section. Supplied pages still pass ordering, encoding, format and
image validation; a failed page cannot be bypassed with reviewer text. Text
fallback applies only when the renderer receives no printable representation;
it does not relax ADR-042 preparation/printing requirements.

This precedence also applies to derived text excerpts in Medical Evidence and
other appendices. Before printing a text derivative, resolve its single immediate
`DerivedFrom` parent and its explicit inclusive `sourceStartPage`/`sourceEndPage`
mapping. If that parent is a native PDF/DOCX, render those original pages from the
content store, retaining their original page numbers. Do not print the derivative's
one-page text representation in place of the mapped native pages. The evidence
artifact, classifications, provenance, ordering and source text remain unchanged;
`PrintableSourceArtifactId` records the representation's origin only for this view.
Reviewer page selection further restricts that mapped range using original source
page coordinates; never silently reinterpret it as offsets into the text derivative.
Unrelated leading pages are outside the excerpt, not omitted blank pages.

The platform range-rendering capability rasterizes only requested PDF pages (and
requested pages of a DOCX's converted PDF), avoiding a full-report image allocation.
Invalid ranges, ambiguous parents, wrong identities, or native rendering failures
are errors, not permission to substitute degraded text. If no bounded native
representation exists, label derived text explicitly as an extracted-text fallback.
Fallback typography does not reconstruct lost form, checkbox or table geometry.
Native page images retain original headers and historical medication entries,
just as other native evidence does; the current/basis-scoped medication summary
and text-fallback medication policy remain unchanged.

Native literature pages use a dedicated full-page layout. The article title,
provenance, and any omitted-blank-page notices appear on an introductory page;
each source image then occupies its own portrait or landscape section. Side
margins are 0.35 inches, with 0.5 inches above and 0.3 inches below for the compact
running header and continuous PAGE/NUMPAGES footer. Images scale proportionally
to the available area, leaving only the inline paragraph's baseline allowance.
Explicit zero drawing distances prevent LibreOffice's default side padding from
offsetting or clipping a full-width image. The next article or appendix restores
the ordinary portrait layout.

PDF rasterization already honors native /Rotate metadata. The PDF provider also
supplies a presentation-only rotation hint when at least 20 non-whitespace native
letters have a dominant baseline direction (80% or more). This identifies a
sideways table inside a portrait MediaBox without confusing a few vertical chart
labels with a rotated article page. The direction is measured after the PDF's
own rotation, preventing double rotation. Ambiguous and image-only pages receive
no inferred rotation. Literature rendering may apply this hint as a lossless
whole-image quarter turn before selecting the page orientation. Every source
pixel, including margins, headers, footers, tables, and charts, is retained;
content-store bytes and the supplied printable-page buffer remain unchanged.
This operation neither reconstructs content nor repairs extracted text.

After orientation, literature presentation may remove exterior whitespace before
scaling. The content bounds include every pixel other than exact opaque white:
faint antialiasing, colored DOI links, isolated marks, and transparent pixels
are retained. Bounds receive a safety margin of one percent of the shorter
original pixel dimension, rounded up, with a two-pixel minimum and clamped to
the original page. All-white and edge-to-edge pages remain unchanged. A pixel
subset is encoded losslessly, without resampling or changing the source buffer.
Publisher headers, footers, DOI links, page numbers, captions, tables, figures,
and references receive exactly the same protection as body text. No semantic
region detection or removal of internal whitespace is permitted.

The reviewer page orientation is selected from the oriented original page before
cropping, retaining landscape placement for sideways source pages even if their
content bounds have a different aspect ratio. The cropped image is then scaled
proportionally to the maximum available reviewer area. Tests compare every
retained pixel at its original integer offset and require every removed pixel
to be exact white, including after quarter-turn orientation. The original
article's own small labels and internal spacing remain; cropping does not
promise a particular readable font size for every source.

Readability review prioritizes tables, figures, forest plots, discussion and
conclusions. The fidelity rules above apply equally to those pages: internal
publisher layout takes priority when no further blank outer margin can be
removed. In particular, an indented reference block does not authorize cropping
non-white publisher furniture or repositioning individual article regions.
Lumbar package page 85 / literature source page 15 is the regression example:
its full source region is already centered with balanced safety padding, while
the reference indentation is internal to the published page. Keep only that
intact page. No supplemental magnification is currently enabled. Any future
magnification must be a general, explicitly labeled feature with objective
selection criteria, separately authorized rather than a page-specific exception.

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

Numeric-only rows, timestamp rows followed by uppercase identifiers, and explicit
repeated ditto columns receive preformatted treatment independently of fragment
length. Whole-row matching leaves numbered headings and quoted prose unchanged;
these recognizers do not infer cells or propagate typography across blank lines.
An explicit timestamp/identifier or ditto row also anchors compact typography for
its following isolated tokens. Any blank, field, multiword structural heading,
divider or prose ends that local context; typography never propagates backwards.
Three or more adjacent identical empty labels also use compact data-row typography,
limited to those labels. A single empty label still stops fragment detection.
Data-row typography does not, by itself, disable ordinary non-Blue-Button prose
reflow when a wrapped sentence happens to contain an isolated number.

Trailing blank text rows at the end of a supplied source page are not rendered as
empty paragraphs. Interior blank rows and stored source text remain unchanged.
A section break uses the final body paragraph when available; a separate minimal
paragraph is needed after a table. This avoids an otherwise empty continuation
page caused solely by terminal whitespace or an extra section-mark paragraph.

Opening reviewer subsections use one shared paragraph-spacing helper with 240
twips of space before a subsection, rather than inserted empty paragraphs.
Chronology entries keep their own wrapped lines together and stay with a following
source reference when present. Entries without references may break between items;
they must not form a keep-with-next chain that displaces the list from its introduction.

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
- Native Medical Evidence retains clinical and patient-identification content.
  The V9 presentation exception below removes only identified Blue Button
  producer footers and moves original word pixels in unambiguous prose; source
  files and stored printable pages remain unchanged.
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
The derived Medical Evidence integration test assembles the package through
`VeteransReviewerPackageDetailsService`, resolves a bounded parent PDF through
the production print router, and converts the resulting DOCX with LibreOffice.
It verifies that the embedded image matches the selected source page, fragmented
extraction is absent from both outputs, original page coordinates survive, and
both stored source and derivative bytes remain unchanged. Resolver regressions
also reject empty, reordered, duplicate, out-of-range, or text-only native results
without rendering the derivative as a substitute.

## Native Medical Evidence presentation refinement (V9/V10)

Medical Evidence no longer prints a redundant `Source Page N` paragraph. The
original source page number remains in the printable model and embedded drawing
metadata. The drawing description also records applied native-page operations.
Continuation images use the available height without reserving a nonexistent
source-label paragraph; aspect ratio and raster resolution are preserved.

For identified Blue Button sources, native PDF glyph coordinates accompany the
raster. On upright, uncropped, text-only pages with unambiguous geometry, the
renderer removes the bottom-margin row only when it matches both the My
HealtheVet generation notice and original page-count boilerplate. It preserves
patient identity, dates, clinical headings, signatures, and all other content.

V10 classifies source regions before moving any clinical content. Native font,
baseline, indentation, paragraph separation, section/form markers, and repeated
column anchors distinguish narrative regions from structured/fixed-layout
regions. A local narrative field label (for example, `Attending:`) does not make
surrounding prose structured. Repeated spaces alone never establish a column:
a candidate gap requires a corroborating native X anchor on another baseline
with a different left cell. Tables, medication lists, vitals, labs, forms,
numbered/bulleted lists, signatures, and fixed clinical sections are protected.
Unknown or overlapping regions are kept as fixed-layout content.

Within proven prose, original raster word tiles may be repositioned to restore
broken wrapping. Word order, spelling and punctuation are retained verbatim;
no OCR, retyping, substitution or clinical inference is used. Native complete-
sentence line boundaries remain boundaries; honorific fragments such as `Dr.`
do not terminate the paragraph. Final single-word fragments may be balanced
without adding words. Every non-white pixel in a candidate band must be
accounted for exactly once, with no destination collisions. Unexplained marks
reject that prose operation.

Structured regions move only as intact raster strips, retaining every internal
X/Y offset and blank line. The isolated `O:` and the objective findings now
remain together in a protected region at their native relative positions;
V9's special heading attachment is superseded. Whitespace compaction is allowed
only in verified all-white gaps between detected regions, never inside one.
Outer cropping retains all remaining ink and the safety margin. This preserves
footer removal and the larger Medical Evidence image area from V9. First-page
Medical Evidence images may use 7.5 inches vertically, replacing the former
7-inch allowance; continuation images retain the 8.5-inch allowance. This gives
native structured spacing room without restoring a redundant source label.

First-page evidence titles and continuation titles share the same blue color
(`365F91`); the continuation adds `— Continued`. This is a shared renderer rule,
not a condition- or artifact-specific exception. Confidentiality headings and
source evidence pixels retain their existing styling. Appendix F source-page
rendering, orientation, cropping, publisher layout and header/footer styling
are unchanged.

Regression coverage includes the package-page-21 Attending paragraph pattern,
repeated spaces, standalone local labels, corroborated column anchors, page-41
medication/vitals spacing, page-42 exam/lab result-unit-reference relationships,
and page-56–58 PT prose alongside protected sections. Fixed regions are compared
pixel-for-pixel under one translation; narrative token sequences are compared
in order. Existing ink-conservation, immutable-source and conservative-fallback
tests remain. Shared-header tests compare the explicit continuation-title color
to the first-page title across appendix types. Actual Lumbar source audits use
original native page rasters; publisher literature images are separately compared
to the accepted V9 package.

## References

- ADR-042: Printable Source Evidence Preservation
- Package Adapter Printing Contract
- ADR-044: Reviewed Medical Literature Promotion Lifecycle

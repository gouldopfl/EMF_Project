# Reviewer Package Determinism Contract

Status: **architectural requirement for all EMF reviewer packages**. This document
defines the required behavior and acceptance criteria. The implementation status
below distinguishes enforced behavior from remaining work. It does not alter the
immutable snapshot V1 wire contract or replace M92/M93 verification.

## 1. Core guarantee

**Frozen Package Snapshot + Explicit Render Settings + Renderer/Converter Version
= Deterministic Visible Document.**

If those inputs are unchanged, repeated rendering must produce the same visible
document content, formatting, pagination, section order, and presentation.
Presentation includes headings, continuation headings, source labels, tables,
images, margins, typography, wrapping, and appendix boundaries.

This is a visible-document guarantee, not a requirement for byte-identical ZIP
containers, generated OPC relationship IDs, PDF file IDs, or technical timestamps.
M92 still validates the exact bytes of each actual output. Byte differences must
not be normalized away in provenance verification.

An explicit render profile must cover presentation-relevant converter settings,
fonts, language/locale, page geometry, and layout-engine dependencies. A converter
version string alone does not establish equivalence between differently configured
environments. Embedded fonts are part of the renderer build. Changing presentation
inputs or the renderer/converter profile requires fresh validation.

Visible generated date labels use the shared profile's explicit formatting, not
the host's ambient culture/calendar. The current profile uses invariant English
regulatory dates and invariant ISO review-check dates. Verbatim source evidence
is preserved rather than translated into the host locale.

## 2. Shared cross-claim presentation

All claims use the same shared presentation rules for equivalent semantic data.
OSA, GERD, lumbar, and future claims use the same shared renderer components where
their semantic data types are equivalent.

Do not introduce formatting keyed to claim ID, condition name, diagnosis name,
page number, veteran, or specific document text. Do not special-case an excerpt
from a motivating Gold package. Recognized semantic labels or standard form types
may select a shared component; they must not become an exact-content workaround.

Shared components cover the cover page, reviewer instructions, regulations,
executive summary, chronology, evidence records, medication records,
questionnaires, continuation headings, appendices, source-document presentation,
and literature. A PAP adherence record, for example, may require its own shared
semantic component whenever that data is available; an OSA condition name must not
activate special formatting. Source-native images and prose may require different
components because their semantic representation differs.

## 3. Single source of truth

Every visible field must come from one resolved/frozen package snapshot, including
Veteran, claim type, claimed condition, basis, reviewer/preparer, visible package
dates, evidence, source labels, diagnoses, medications, questionnaires, chronology,
literature, and appendix placement.

Resolution and sourcing happen before freezing. The renderer must not independently
rediscover, reinterpret, or source visible claim information during rendering.
Environment variables may supply preparation inputs before capture; they must not
override a preserved snapshot during print/export. A typed semantic value must
not be reconstructed from a rendered opinion sentence when a resolved value is
available. Unknown or missing values must not be invented.

The canonical prepared-package aggregate consists of the unchanged V1 source
snapshot, an immutable versioned presentation snapshot, and its frozen PDF when
prepared. The presentation snapshot binds the source SHA-256, resolved typed cover,
`PackagePreparedDate`, shared render profile, preparation renderer identity, and
compiled DOCX plan. The frozen PDF binds that presentation hash and concrete
converter/profile identity. SQLite migration 94 adds these append-only records.
New presentations additionally freeze a figure/table-region audit alongside the
compiled document. Detected candidates retain confidence and rejection reasons;
selected regions become Frozen. The audit binds exact oriented privacy-masked
raster SHA-256 and transform version, source artifact/page, raster dimensions and
orientation, top-left half-open pixel bounds, applied rotation, detection method
and version, magnification, exact display dimensions, crop SHA-256, and renderer
build. Before freezing, preparation verifies the crop pixels and display scale
against the bound source raster. Reprint returns the frozen document directly:
no redetection, recropping or reinterpretation. A new presentation may detect again.
The optional audit field is absent in older snapshots, preserving their existing
hashes. The source snapshot V1 wire contract remains unchanged.

Historical V1 payloads and M92/M93 records are not rewritten or reinterpreted at
reprint. Missing historical scope remains missing unless explicitly resolved during
preparation. A legacy adapter may interpret only the frozen V1 opinion once during
preparation; print/export does not parse the opinion.

## 4. Pure rendering

Print/export is a rendering operation. The renderer consumes frozen package data,
explicit render settings, and renderer/converter identity. It does not query live
claim data, refetch regulations, invoke AI, infer a claim from a diagnosis, select
new evidence, or make new claim-data decisions while laying out the document.

Layout decisions remain necessary: wrapping a paragraph, repeating a continuation
heading, or paginating a long table does not change the resolved semantic data.
Source normalization, evidence associations, medication reconciliation, appendix
classification, and clinical interpretation should be resolved and frozen before
the pure presentation boundary. Preserve source evidence and privacy protections.

`VeteransReviewerPackagePresentationPreparation.Prepare` validates the source,
resolves legacy records through the existing shared components, and compiles the
immutable OpenXML document plan. Source interpretation and layout compilation
occur once here. It requires a nondefault explicit `PackagePreparedDate`.

`VeteransReviewerPackageDocxRenderer.RenderPrepared` is pure printing: it validates
and materializes the plan without a clock, evidence interpretation, live data,
or new claim decisions. PDF conversion is an initial preparation operation; later
exports materialize the frozen PDF rather than reconverting it. The persisted
production output service uses these boundaries. `RenderSnapshot` is an explicit
prepare-and-materialize convenience for synthetic/compatibility callers.

The details-based `Render` API is the shared compilation API, not a preserved
print operation. It now rejects omitted/default dates; it has no implicit clock.
Synthetic legacy fixtures use one explicit fixture preparation date. Production
callers use explicit preparation and `RenderPrepared`. Reprints must never use
this compilation overload to reconstruct a preserved presentation.

## 5. Reprint and dates

Distinguish `PackagePreparedDate` and preserved visible dates from `GeneratedUtc`,
the technical output-generation audit timestamp.

A true preserved reprint retains all original visible dates, including regulatory
retrieval/current-through dates, evidence dates, reconciliation dates, and the
package date used by visible source checks. Rendering must not silently refresh
these dates. A newly prepared version may retain evidence dates while explicitly
refreshing preparation/retrieval inputs.

Refreshing a visible date is an input change and creates a new package version.
It must not append a differently dated output under the original preserved package
identity. The output service rejects a date refresh for a package with a recorded
date and retains the original date when a reprint omits a new date. Repository
saves enforce the same rule under an immediate SQLite transaction across output
formats and builds. Conflicting historical recorded dates fail closed rather
than choosing one arbitrarily. No historical record is modified.

The presentation snapshot owns `PackagePreparedDate`; M92 `SourceReviewDate`
records the same input and must agree. This is not a newly added cover-date line.
A first preparation may resolve today's date once and freeze it. A historical V1
snapshot without a presentation or reliable recorded date fails closed unless
an explicit preparation date is supplied. A known provenance date is retained
when preparing a legacy snapshot; it is not replaced with today's date.

`VeteransReviewerPresentationVersionService.PreserveAsync` returns only a frozen
presentation. `PrepareNewVersionAsync` requires an explicitly resolved source
snapshot, explicit dates, typed cover, a fresh package identity in the same claim
issue, and verified deployment identity. It stores the new immutable source and
presentation with a link to the prior package. Upstream preparation must explicitly
refresh any desired regulatory/source dates; this operation does not refetch them.
It never alters the original package. Interrupted preparation can leave a pending
new identity; retry/recovery must validate it rather than overwrite it.

`GeneratedUtc` may change for a newly created physical output without changing
preserved visible presentation. An exact-byte reuse retains the original output's
provenance; a later reprint action may have its own audit event. Never alter an
immutable provenance record's original generation timestamp.

## 6. Data changes

Different amounts of evidence can legitimately produce different pagination.
Long medication directions or evidence prose can wrap differently. Adding/removing
semantic sections can change section placement. These are input differences.

Unchanged data must not produce arbitrary formatting or pagination changes.
Equivalent semantic structures must retain the same style, spacing, width,
keep-with-next, row-splitting, and continuation rules across all claims. Do not
shrink text or omit data merely to match another claim's page count.

## 7. Change control

> A reviewer-rendering change must not be accepted solely because the claim
> that motivated the change looks correct. It must also pass repeat-render
> determinism and cross-claim presentation regression tests.

Use the smallest shared fix supported by the comparison. Preserve unrelated
privacy, source-preservation, medication, and provenance work. An observed Gold
layout match does not establish complete architectural compliance.

## 8. Required tests and acceptance

Future renderer changes must pass:

- Repeat-render DOCX tests: compare all visible document parts, styles, settings,
  headers/footers, images, section controls, and relationship targets. Normalize
  only technical container metadata and generated IDs; retain semantic order.
- Repeat-render PDF tests: compare page count, page dimensions, text, word
  positions within a declared tolerance, section placement, continuation
  headings, and the absence of unexpected blank pages and clipping. Current
  synthetic checks use a 0.1-point repeat-conversion tolerance.
- Cross-claim tests: primary/secondary configurations, multiple synthetic
  conditions and medicines, questionnaire types, short/long evidence, and
  multiple appendix combinations. Assert available sections and content survive.
- Date tests: explicit visible-date changes are input differences; preserved
  reprints keep dates; date refresh requires a distinct package version; technical
  audit timestamp changes do not affect preserved presentation.
- Relevant renderer, source-presentation, provenance, and deployment regression
  tests. Tampered output and unverified binaries must still fail verification.
- Gold-reference comparison where applicable, using verified external inputs.

Tests must actually be discovered and executed. Report exact pass/fail/skip counts,
build failures, and `git diff --check`. A skipped PDF check does not satisfy the
required PDF gate. Run with `EMF_REVIEWER_LAYOUT_TESTS=true` and the controlled
LibreOffice installation; keep Azure AI disabled. Example focused invocation:

```bash
env -u EMF_AZURE_OPENAI_LIVE -u EMF_REVIEWED_BY \
  EMF_REVIEWER_LAYOUT_TESTS=true \
  dotnet test tests/EMF.Tests/EMF.Tests.csproj \
  --filter 'FullyQualifiedName~VeteransReviewerPackageDeterminismTests|FullyQualifiedName~VeteransReviewerClinicalLayoutTests|FullyQualifiedName~VeteransReviewerOutputProvenance'
```

This command illustrates contract tests, not the entire acceptance gate. Include
the relevant renderer/presentation and deployment suites for the actual change.

## 9. Approved Gold packages

Approved OSA, GERD, and Lumbar packages may be external validation references.
Verify their authoritative identity/hash before claiming a comparison. Do not
substitute a historical version merely because its filename appears suitable.
Do not regenerate, modify, or overwrite Gold during verification.

Do not store private medical documents or personal medical data in Git. Keep
production tests synthetic and do not hard-code Gold-specific content into the
renderer. Approval hashes and private comparison reports belong in the external
verification workspace when they include private details.

The previously reported lumbar result was 87 Gold pages / 87 candidate pages,
presentation PASS with only expected dated eCFR citations differing, and 364 tests
passing. That result belongs to its verified candidate build. No new real-package
comparison is implied by this documentation or by synthetic test success.

## 10. Provenance

M92/M93 provenance remains separate from visible presentation. Build/output
provenance records what created an output; it must not create document-layout
differences. Do not weaken or redesign provenance to obtain repeatability.

Preserve snapshot integrity/membership validation, exact-byte hashes, immutable
records, verified build identity, converter identity/version, linked build
manifests, atomic saves, and fail-closed behavior. Render equality is not a reason
to reuse bytes that fail provenance verification. A new renderer build still
requires its own verified deployment before generating new outputs.

## 11. Future UI and schema

Intended UI:

```text
Reprint Existing Package
[ ] Preserve original package dates
[ ] Refresh visible dates and create a new package version
```

These represent mutually exclusive modes. Preserve should be the default for a
reprint. The UI must display the preserved package/version and dates. Refresh must
prepare a new version, resolve/freeze its visible dates and data, and retain a
link to the original. Existing packages and output provenance remain immutable.
The precise UI implementation is deferred; this behavior is required now.

Implemented schema/application boundaries:

1. Typed cover scope is resolved from selected theory/basis/condition and medication
   records during new-package assembly. Equivalent primary/secondary cover fields
   use the same renderer. Unselected/unknown theory is not inferred as primary.
2. Migration 94 stores immutable prepared dates, render profile, resolved cover,
   compiled DOCX, source hash and previous-package lineage. V1 remains unchanged.
3. Source normalization, clinical/source labels, medication/questionnaire handling,
   chronology, appendices and literature are compiled once into the presentation.
   Preserved printing does not rerun these decisions.
4. The Linux LibreOffice profile fixes private user settings, en-US document locale,
   C.UTF-8 process locale, UTC, the headless `svp` backend at 96 DPI, explicit font directories,
   and the PDF export filter. Its identity hashes installed converter program files,
   registry, selected fallback fonts, shaping dependencies, OS and architecture.
   Ambient desktop/font/loader override variables are removed in the child process.
   This profile fingerprint extends the recorded converter version. Before/after
   conversion identities must agree. Unsupported platforms fail closed. A controlled
   converter environment remains necessary for preparing a new PDF; a frozen PDF
   itself has no fresh pagination. Profile changes under an existing frozen PDF
   are rejected before conversion and require a new version.
5. The explicit preserve/new-version service creates the package identity, artifact
   membership, source snapshot and frozen presentation in one immediate SQLite
   transaction. Failed or cancelled creation rolls back all four; the same fresh
   identity can then be retried. Repositories without atomic creation support fail
   before any version writes. UI controls, package listings, lineage display and
   date selection can be implemented separately without changing the contract.
   Explicit `RecoverIncompleteVersionAsync` can complete a caller-reviewed existing
   incomplete identity in one transaction. Its membership and any sealed source
   must match exactly; existing presentations, legacy identities and output history
   are rejected. Recovery does not run automatically or choose historical inputs.

## Current implementation status

| Contract area | Architecture after this change |
| --- | --- |
| Frozen canonical input | Immutable source + presentation + prepared PDF aggregate, bound by SHA-256. V1 compatibility is retained without rewriting history. |
| Shared cross-claim presentation | One shared compilation pipeline, typed semantic scope, shared components and synthetic cross-claim regression matrix. |
| Single source of truth | New preparation resolves typed cover from records; legacy adapter uses frozen V1 only. Every preserved visible field is in the compiled plan/PDF. |
| Deterministic dates | Prepared date is persisted before provenance. Source dates stay frozen. Refresh requires a distinct package identity; GeneratedUtc remains audit metadata. |
| Pure print/export | Prepared DOCX/PDF materialization makes no source, clinical, regulatory or pagination decisions. |
| Renderer/converter identity | Renderer contract v2; preparation build retained. Controlled Linux converter profile recorded and checked; M92/M93 exact-byte/build checks retained. |
| Data-vs-render differences | Different evidence or explicit dates require new preparation. Reprints do not reinterpret unchanged evidence. |
| Persistence controls | Append-only SQL triggers, source/presentation linkage, date agreement, exact plan/output binding and idempotent exact saves. |
| UI | Application preserve/new-version boundary implemented; UI implementation deferred. |
| Gold proof | Prior lumbar comparison remains historical. No new Gold comparison is claimed for this architecture/build. |

The architecture can be reviewed/frozen as the shared working baseline once the
listed automated gates pass. Production deployment approval still requires a
separately verified candidate build/manifest and applicable external Gold
comparisons. No unit-test result substitutes for those comparisons. Do not
silently adopt a new renderer/profile for a historically preserved package.

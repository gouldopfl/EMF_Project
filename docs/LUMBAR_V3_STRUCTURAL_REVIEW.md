# Lumbar V3 structural review

No V4 generation, commit, paid-model call, source rewrite, or production-data modification. Azure OpenAI was NOT invoked. All SQLite inspection used read-only connections; diagnostic copies and disposable source/test conversions were confined to /tmp.

## Confirmed causes

1. June 22 ED: two derived clinical-note artifacts from parent `9709e0900b3b459b8828a1d457893e27`, with overlapping source selections 504–517 and 505–517. Both are package members. Boundary-page glyphs of the smaller projection are exact subsets of the larger; interior pages match exactly. Presentation now keeps the containing projection once. Source records and membership ledger remain unchanged.
2. March ED / Clinical Triage reflow: the narrative-density fallback required 30% narrative rows. These real records have approximately 16% and 26%, respectively. Both have verifiable aligned narrative. Page-sized images, including a title-only source page, therefore survived the previous change. Eligibility now requires aligned adjacent prose, with field/table/scalar/divider/signature boundaries retained. Opening metadata keeps with the note.
3. July 31 medications: both heading detection and exclusion missed the `MEDS:` prefix. The heading detector also concatenated glyphs without restoring geometric spaces. Real-page audit excludes 107 bounded historical table rows while retaining clinical sections and original pages.
4. July/August PT: July 31 was duplicated by selections 434–441 and 435–440. The smaller projection is contained exactly. August 17 is separate, 419–420. Matching referral/goals are carry-forward; unique response includes improved lumbar AROM, less pain prone, slightly less pain with press-ups, reassessment and patient-reported improvement. August stays intact.
5. Privacy: optional zero-width FIN separator plus alphanumeric values matched `fingernails`, and broad ID conversion created misleading last-four labels. Internal MRN/FIN/EDIPI/ICN fields are now suppressed; only existing specifically permitted SSN-shaped identifiers retain last-four behavior. Reordered PowerForm labels are handled as explicit whole field clusters. Sanitization runs before text layout.
6. September PowerForm: a text/plain export, not a native multi-column document. Generic row classifiers switched between 9-point monospace and 12-point body, and inferred field/value layouts from flattened text. A producer-specific conservative path now uses one body font, preserves order, removes proven repeated administrative headers, and does not assign Goal/Status/Date Met/provider timestamps to columns.
7. Pain scales: joining detached lines did not make the label explicit. Existing joiner is retained and extended to use `Pain number from 0-10` only after a numeric-pain label or pain-specific question. Actual values remain 7, 3, 7, 5, 5, 5.
8. Labels/line breaks: known producer labels can span lines; exact semantic joins handle STANDARD TITLE and pain-question labels. Geometric column spacing and explicit boundaries remain protected. No generic short-line joining was added.
9. Appendix D: secure messaging and separate AFO clarification were both persisted as LayEvidence. Presentation membership routes the clinical artifact to Medical Evidence and the separately named clarification to Additional Evidence. Only Spousal Statement and Veteran Personal Statement remain in D. Redundant source-page labels are removed for lay statements.
10. Literature: only Abbasi has a supplied registered artifact and active reviewed classification. Other citations exist as registrations and 3.310(a) relationships, not article evidence. No production relationships were changed.
11. Cover: explicit claim-type, condition, and basis labels were absent. Exact condition and basis wording are taken from the existing deterministic medical-opinion request; no internal ID is exposed.

## Veteran statement clipping trace

The original source DOCX located under `EMF_Input/docs/` matches the stored artifact SHA-256. It contains the complete typed ending inside a fixed-height VML text box (474 × 580.45 pt), plus a separate embedded signature image. A disposable conversion of this original alone reproduces the clipping. The stored printable-page image already lacks the ending. V3 embeds byte-identical stored PNG bytes and has no image crop rectangle. The final V3 PDF displays the same incomplete image; reviewer placement/final PDF conversion did not cause the missing typed text.

No complete upstream full-page image exists in that DOCX. Preserve the existing image and signature bytes. Supplement only the two verified incomplete/missing typed sentences from independently persisted text: the final medical-review request (whole sentence for context) and the typed certification. Stop before signer/signature/date content. The supplement stays within the existing Veteran Personal Statement evidence item, with explicit source and reviewer note.

## Persisted literature relationships

| Registration | Supplied artifact | issue-lumbar link | basis-lumbar-flat-feet / 3.310(a) | 3.310(b) | Active reviewed classification |
|---|---|---|---|---|---|
| Kosashvili 2008 | none registered | via basis | SupportsRequirement | none | none |
| Stone 2024 VA planus/gait | none registered | via basis | SupportsRequirement | none | none |
| Yazdani 2019 hyperpronation/lumbopelvic | none registered | via basis | SupportsRequirement | none | none |
| Abbasi 2024 | 3b5a4828044a4b829887f79d44ac7c84 | via basis | SupportsRequirement and Clarifies | none | Clarifies |

The first three need independently supplied artifact attachments and active reviewed classifications before article selection can include them. Any proposed 3.310(b) association also needs an explicit evidence/review decision. References inside Abbasi are not supplied articles. The existing selector is correct for current persisted data.


## Files changed in this work

Production code, under `src/EMF.Extensions.VeteransClaims.Orchestration/`:

- `VeteransReviewerSourceMembership.cs` (new): exact lineage/page/glyph containment; presentation membership and routing.
- `VeteransReviewerNativeEvidencePage.cs`: recognize MEDS-prefixed historical medication tables.
- `VeteransReviewerNativeProse.cs`: aligned narrative eligibility; preserve structured boundaries; exact form-label reconstruction; extend existing pain-scale joiner.
- `VeteransReviewerPackagePrivacySanitizer.cs`: true field boundaries, internal-ID suppression and reordered PowerForm fields.
- `VeteransReviewerPowerForm.cs` (new): conservative ordered text presentation and identified producer-header cleanup.
- `VeteransReviewerLayTextSupplement.cs` (new): exact stored/native text comparison; missing typed sentences only; stop at certification.
- `VeteransReviewerPackageDocxRenderer.cs`: apply selection/routing; pre-layout privacy; cover labels; consistent PowerForm font; metadata keep-with-next; lay supplement within the existing item; remove redundant lay source-page labels; recognize geometrically spaced medication headings.

Tests, under `tests/EMF.Tests/`:

- `VeteransReviewerLumbarStructureTests.cs` (new): 20 regression cases, including two real LibreOffice pagination/conversion tests with synthetic sources. Covers ED/PT containment, preserving separate lineage/new findings, Appendix D membership, MEDS exclusion with source preservation, PowerForm typography/order/header cleanup, pain-specific labels and negatives, protected fields/tables/scalars/signatures, cover terminology, source-title/first-content pagination, supplemental-text provenance/membership/nonduplication, and bottom-of-page final text/signature-region preservation.
- `VeteransReviewerNativeProseTests.cs`: existing pain joiner test now requires an explicit pain label; structured Assessment boundary preserved rather than merged into previous field.
- `VeteransReviewerPackagePrivacySanitizerTests.cs`: internal-ID suppression, exact real failure shapes, ordinary fingernails/FINancial prose preservation, permitted SSN/MRN last-four behavior, reordered PowerForm identifiers.

Documentation: `docs/LUMBAR_V3_STRUCTURAL_REVIEW.md` (this report).

The pre-existing uncommitted changes were preserved. This work did not additionally edit the already-modified `VeteransReviewerNativeEvidencePageTests.cs`, `VeteransReviewerPackageDocxRendererTests.cs`, or `VeteransReviewerPresentationPhase2Tests.cs`. Their existing regressions were included in validation. Existing untracked EMF_Output and handoff/audit files were not changed.

## Remaining limitations

- Three literature registrations still lack article attachments and active reviewed classifications; no 3.310(b) literature associations exist. Production relationships remain untouched.
- The September PowerForm has no reliable goal/status/date/provider column associations. These remain explicitly unresolved, in source order, at a consistent reviewer font.
- The original statement image remains clipped because of the source DOCX text-box layout. Its bytes are preserved. The verified two-sentence supplement restores access to the typed ending, without constructing signature/date content or a third Appendix D item.
- No full lumbar package has been regenerated or approved. Actual V4 pagination and visual review remain to be checked after authorization; tests exercise structural pagination without hard-coded reviewer page numbers.

## Validation

Final focused run: **273 passed, 0 failed, 0 skipped (273 total)**, including the two disposable LibreOffice layout/conversion checks. Filter includes VeteransReviewerLumbarStructureTests, VeteransReviewerNativeProseTests, VeteransReviewerPackagePrivacySanitizerTests, VeteransReviewerNativeEvidencePageTests, VeteransReviewerPresentationPhase2Tests, and VeteransReviewerPackageDocxRendererTests.

Every validation command explicitly set `EMF_AZURE_OPENAI_LIVE=false`, `EMF_AZURE_OPENAI_LIVE_TESTS=false`, and `EMF_REVIEWER_LAYOUT_TESTS=true`. The broader run uses `dotnet test tests/EMF.Tests/EMF.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~VeteransReviewer'`. No model call was made; AI cost for this work was $0.00000.

Broader Veterans reviewer/presentation run: **717 passed, 0 failed, 0 skipped (717 total)**. Focused tests are included in the broader total, not additional independent cases. `git diff --check` passes with no output. HEAD remains `011607ee`; no commit was created.

## Recommendation

Code is ready for an authorized V4 regeneration and visual review. This is not approval of the resulting package. Literature completeness still requires the missing supplied articles and reviewed classifications described above; no production relationship should be invented to fill that gap. No V4 has been generated.

## Final git status --short

```text
 M src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerNativeEvidencePage.cs
 M src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerNativeProse.cs
 M src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackageDocxRenderer.cs
 M src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPackagePrivacySanitizer.cs
 M tests/EMF.Tests/VeteransReviewerNativeEvidencePageTests.cs
 M tests/EMF.Tests/VeteransReviewerNativeProseTests.cs
 M tests/EMF.Tests/VeteransReviewerPackageDocxRendererTests.cs
 M tests/EMF.Tests/VeteransReviewerPackagePrivacySanitizerTests.cs
 M tests/EMF.Tests/VeteransReviewerPresentationPhase2Tests.cs
?? EMF_Output/
?? HARDENING_HANDOFF.md
?? HARDENING_REVIEW.md
?? SECURITY_HIPAA_NIST_AUDIT.md
?? docs/LUMBAR_V3_STRUCTURAL_REVIEW.md
?? handoff/
?? src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerDateOfBirth.cs
?? src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerLayTextSupplement.cs
?? src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerPowerForm.cs
?? src/EMF.Extensions.VeteransClaims.Orchestration/VeteransReviewerSourceMembership.cs
?? tests/EMF.Tests/VeteransReviewerLumbarStructureTests.cs
```

## Supplemental small presentation pass

- Added `VeteransReviewerDateOfBirth.cs`: reconstructs only an explicitly labelled month/day ending in a comma followed immediately by a standalone four-digit year. Date words/digits are preserved; nonbreaking spaces keep the complete value together. No general comma-line joining is used. The renderer applies reconstruction before layout and atomic spacing before field parsing/final text emission.
- `VeteransReviewerPackageDocxRenderer.cs`: known STANDARD TITLE and pain-question labels now use full-width paragraphs. Their previously reconstructed text could still wrap into fragments inside the narrow label column of a field table. Other field/table reconstruction remains unchanged.
- The same renderer now places Additional Evidence inside the first additional item's section, removing the immediate next-page section break that could strand its heading. No appendix membership changes were made in this supplemental pass.
- Extended `VeteransReviewerLumbarStructureTests.cs`: six DOB cases cover the requested April date, another month/year, CRLF, and rejected unrelated/boundary cases. A new regression checks full-width known labels. The existing LibreOffice pagination regression now also verifies signature-only source-page reflow, DOB month/year on the same PDF baseline, and Additional Evidence heading/content on the same PDF page. **27 targeted cases passed, 0 failed, 0 skipped.**
- The page-87 reference does not match the existing V3 PDF: that page contains the lidocaine-message continuation. The reproducible Additional Evidence heading/section-break defect was fixed without relying on page numbers.

This remains a presentation-only pass. No V4 generation, commit, paid model call, or persisted-source change.

Final supplemental validation: **27 targeted tests passed; 724 broader Veterans reviewer/presentation tests passed; 0 failures and 0 skips in both runs.** The broader total supersedes the earlier 717-test result and includes the targeted cases. `git diff --check` remains clean. Azure OpenAI was NOT invoked; AI cost remains $0.00000. Code remains ready for authorized V4 regeneration and visual review, subject to the unchanged content limitations above.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPackageDocxRenderer
{
    public static byte[] Render(
        VeteransReviewerPackageDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var package =
            details.PackageDetails.Package;

        ValidatePackageContents(
            details,
            package);

        using var stream =
            new MemoryStream();

        using (var document =
            WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document))
        {
            var mainPart =
                document.AddMainDocumentPart();

            var footerPart =
                mainPart.AddNewPart<FooterPart>();

            footerPart.Footer =
                ReviewerFooter();

            var footerRelationshipId =
                mainPart.GetIdOfPart(footerPart);

            var settingsPart =
                mainPart.AddNewPart<DocumentSettingsPart>();

            settingsPart.Settings =
                new Settings(
                    new UpdateFieldsOnOpen
                    {
                        Val = true
                    });

            var body =
                new Body(
                    ConfidentialParagraph(),
                    StyledParagraph(
                        "Veterans Evidence Reviewer Report",
                        "Title"),
                    StyledParagraph(
                        $"Purpose: {package.Purpose}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Reviewer Role: {ReviewerRoleDisplayName(package.ReviewerRole)}",
                        "Subtitle"),
                    PageBreakParagraph());

            AppendExecutiveSummary(
                body,
                details);

            body.Append(PageBreakParagraph());

            AppendPackageGuide(
                body,
                details);

            body.Append(PageBreakParagraph());

            AppendReviewScope(
                body,
                details);

            AppendMedicationProgressions(
                body,
                details);

            AppendPrescribedMedications(
                body,
                details);

            AppendKeyEvidenceAndChronology(
                body,
                details);

            AppendMedicalLiteratureConsidered(
                body,
                details);

            body.Append(PageBreakParagraph());

            AppendReviewerQuestions(
                body);

            AppendEvidenceAppendices(
                mainPart,
                body,
                details);

            body.Append(
                new SectionProperties(
                    new FooterReference
                    {
                        Type = HeaderFooterValues.Default,
                        Id = footerRelationshipId
                    },
                    new PageMargin
                    {
                        Top = 1440,
                        Right = 1440U,
                        Bottom = 1440,
                        Left = 1440U,
                        Footer = 720U
                    }));

            mainPart.Document =
                new Document(body);
        }

        return stream.ToArray();
    }

    private static void ValidatePackageContents(
        VeteransReviewerPackageDetails details,
        EvidencePackage package)
    {
        var packageArtifacts =
            details.PackageDetails.Artifacts;

        foreach (var packageArtifact in packageArtifacts)
        {
            if (packageArtifact.EvidencePackageId != package.Id)
            {
                throw new InvalidOperationException(
                    $"Evidence package '{package.Id.Value}' contains artifact " +
                    $"'{packageArtifact.ArtifactId.Value}' associated with " +
                    $"package '{packageArtifact.EvidencePackageId.Value}'.");
            }

            if (packageArtifact.ContentRole is not (
                EvidencePackageContentRoles.UnderlyingEvidence or
                EvidencePackageContentRoles.GeneratedOrganizationalMaterial))
            {
                throw new InvalidOperationException(
                    $"Evidence package '{package.Id.Value}' contains unsupported " +
                    $"content role '{packageArtifact.ContentRole}'.");
            }
        }

        var duplicatePackageArtifact =
            packageArtifacts
                .GroupBy(artifact => artifact.ArtifactId)
                .FirstOrDefault(group => group.Count() > 1);

        if (duplicatePackageArtifact is not null)
        {
            var roles =
                duplicatePackageArtifact
                    .Select(artifact => artifact.ContentRole)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

            var description =
                roles.Length > 1
                    ? "conflicting content roles"
                    : "duplicate package entries";

            throw new InvalidOperationException(
                $"Evidence package '{package.Id.Value}' contains {description} " +
                $"for artifact '{duplicatePackageArtifact.Key.Value}'.");
        }

        var duplicateContent =
            details.ArtifactContents
                .GroupBy(content => content.Artifact.Id)
                .FirstOrDefault(group => group.Count() > 1);

        if (duplicateContent is not null)
        {
            throw new InvalidOperationException(
                $"Evidence package '{package.Id.Value}' has multiple reviewable " +
                $"content entries for artifact '{duplicateContent.Key.Value}'.");
        }

        foreach (var content in details.ArtifactContents)
        {
            if (!packageArtifacts.Any(
                    packageArtifact =>
                        packageArtifact.ArtifactId ==
                            content.Artifact.Id))
            {
                throw new InvalidOperationException(
                    $"Evidence package '{package.Id.Value}' received reviewable " +
                    $"content for artifact '{content.Artifact.Id.Value}' that is " +
                    "not part of the package.");
            }

            var mismatchedProvenance =
                content.Provenance
                    .FirstOrDefault(
                        provenance =>
                            provenance.ArtifactId != content.Artifact.Id);

            if (mismatchedProvenance is not null)
            {
                throw new InvalidOperationException(
                    $"Reviewer artifact '{content.Artifact.Id.Value}' contains " +
                    $"provenance for artifact " +
                    $"'{mismatchedProvenance.ArtifactId.Value}'.");
            }

            var unrelatedRelationship =
                content.Relationships
                    .FirstOrDefault(
                        relationship =>
                            relationship.SourceArtifactId != content.Artifact.Id &&
                            relationship.TargetArtifactId != content.Artifact.Id);

            if (unrelatedRelationship is not null)
            {
                throw new InvalidOperationException(
                    $"Reviewer artifact '{content.Artifact.Id.Value}' contains " +
                    "an unrelated artifact relationship.");
            }

            var mismatchedReviewedLiterature =
                content.ReviewedMedicalLiteratureClassifications
                    .FirstOrDefault(
                        reviewed =>
                            reviewed.ArtifactId != content.Artifact.Id);

            if (mismatchedReviewedLiterature is not null)
            {
                throw new InvalidOperationException(
                    $"Reviewer artifact '{content.Artifact.Id.Value}' contains " +
                    $"reviewed literature for artifact " +
                    $"'{mismatchedReviewedLiterature.ArtifactId.Value}'.");
            }

            var mismatchedExcerpt =
                content.ReviewedMedicalLiteratureClassifications
                    .SelectMany(reviewed => reviewed.SourceExcerpts)
                    .FirstOrDefault(
                        excerpt =>
                            excerpt.ArtifactId != content.Artifact.Id);

            if (mismatchedExcerpt is not null)
            {
                throw new InvalidOperationException(
                    $"Reviewer artifact '{content.Artifact.Id.Value}' contains " +
                    $"a reviewed literature excerpt for artifact " +
                    $"'{mismatchedExcerpt.ArtifactId.Value}'.");
            }
        }

        foreach (var clarification in details.SourceClarifications)
        {
            if (!details.ArtifactContents.Any(
                    content =>
                        content.Artifact.Id == clarification.ReviewerArtifactId &&
                        packageArtifacts.Any(
                            packageArtifact =>
                                packageArtifact.ArtifactId == content.Artifact.Id &&
                                string.Equals(
                                    packageArtifact.ContentRole,
                                    EvidencePackageContentRoles.UnderlyingEvidence,
                                    StringComparison.Ordinal))))
            {
                throw new InvalidOperationException(
                    "Reviewer source clarification is not associated with " +
                    "underlying evidence in the package.");
            }

            if (string.IsNullOrWhiteSpace(clarification.SourceLocator) ||
                string.IsNullOrWhiteSpace(clarification.OriginalText) ||
                string.IsNullOrWhiteSpace(clarification.Clarification))
            {
                throw new InvalidOperationException(
                    "Reviewer source clarification is incomplete.");
            }
        }

        foreach (var packageArtifact in packageArtifacts)
        {
            if (details.ArtifactContents.Any(
                    content =>
                        content.Artifact.Id ==
                            packageArtifact.ArtifactId))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Evidence package '{package.Id.Value}' has no reviewable " +
                $"content for artifact '{packageArtifact.ArtifactId.Value}'.");
        }
    }

    private static IReadOnlyList<VeteransReviewerArtifactContent> GetRoleContents(
        VeteransReviewerPackageDetails details,
        string contentRole) =>
        details.ArtifactContents
            .Where(
                content =>
                    details.PackageDetails.Artifacts.Any(
                        packageArtifact =>
                            packageArtifact.ArtifactId ==
                                content.Artifact.Id &&
                            string.Equals(
                                packageArtifact.ContentRole,
                                contentRole,
                                StringComparison.Ordinal)))
            .ToArray();

    private static void AppendExecutiveSummary(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var package =
            details.PackageDetails.Package;

        var sourceCount =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence).Count;

        body.Append(
            StyledParagraph(
                "Executive Summary",
                "Heading1"));

        body.Append(
            StyledParagraph(
                "Purpose of This Document",
                "Heading2"));

        body.Append(
            ContentParagraph(
                $"Purpose: {package.Purpose}. This package organizes the evidence " +
                "supplied for independent medical review and is intended to help the " +
                "reviewing medical professional locate and evaluate the relevant " +
                "medical, lay, adjudicative, treatment-device, and medical/scientific " +
                "evidence efficiently. It does not make a medical, legal, or " +
                "adjudicative conclusion."));

        if (!string.IsNullOrWhiteSpace(
                details.MedicalOpinionRequested))
        {
            body.Append(
                StyledParagraph(
                    "Medical Opinion Requested",
                    "Heading2"));

            body.Append(
                ContentParagraph(
                    details.MedicalOpinionRequested));
        }

        body.Append(
            StyledParagraph(
                "How to Use This Package",
                "Heading2"));

        body.Append(
            ContentParagraph(
                "Review the Issues Presented for Medical Review and Questions for the " +
                "Reviewing Physician first. Use the Key Evidence and Chronology for " +
                "orientation to the principal medical evidence, then use the " +
                "appendices to review the underlying source material. " +
                "Medical/scientific literature is reproduced in Appendix E."));

        body.Append(
            StyledParagraph(
                "Reviewer Guidance",
                "Heading2"));

        body.Append(
            ContentParagraph(
                "Base any opinion on the evidence supplied, identify the specific " +
                "records and medical/scientific literature relied upon, address " +
                "medically applicable causation and aggravation questions separately, " +
                "and explain the medical rationale for each opinion."));

        body.Append(
            ContentParagraph(
                $"Evidence sources supplied for review: {sourceCount}."));
    }

    private static void AppendPackageGuide(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        body.Append(
            StyledParagraph(
                "Package Guide",
                "Heading1"));

        AppendPackageGuideEntry(
            body,
            "Issues Presented for Medical Review",
            "Defines the review purpose, reviewer role, evidence scope, and limitations.");

        if (details.MedicationProgressions.Count > 0)
        {
            AppendPackageGuideEntry(
                body,
                "Relevant Medication Progression / History",
                "Summarizes meaningful dose, direction, and prescription-status changes for medications relevant to the medical opinion request.");
        }

        if (details.CurrentMedications.Count > 0)
        {
            AppendPackageGuideEntry(
                body,
                "Current Medication List",
                "Provides the complete current prescription snapshot from the newest complete VA medication ledger.");
        }

        AppendPackageGuideEntry(
            body,
            "Key Evidence and Chronology",
            "Provides chronological orientation to the principal medical evidence.");

        AppendPackageGuideEntry(
            body,
            "Medical / Scientific Literature Considered",
            "Identifies literature supplied for consideration during the medical review.");

        AppendPackageGuideEntry(
            body,
            "Questions for the Reviewing Physician",
            "Lists the medical questions the reviewing physician is asked to address.");


        var appendices =
            GetRoleContents(
                    details,
                    EvidencePackageContentRoles.UnderlyingEvidence)
                .Where(content => content.Appendix is not null)
                .Select(content => content.Appendix!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(AppendixOrder)
                .ToArray();

        foreach (var appendix in appendices)
        {
            var description =
                appendix switch
                {
                    VeteransReviewerPackageAppendix.MedicalEvidence =>
                        "Contains clinical notes, diagnostic reports, treatment records, and related medical evidence.",
                    VeteransReviewerPackageAppendix.ServiceRecords =>
                        "Contains relevant military service and service-treatment evidence.",
                    VeteransReviewerPackageAppendix.LayEvidence =>
                        "Contains statements and observations from the veteran and other lay witnesses.",
                    VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                        "Contains relevant VA decisions and adjudicative records.",
                    VeteransReviewerPackageAppendix.MedicalLiterature =>
                        "Contains the medical/scientific literature supplied for review.",
                    _ =>
                        "Contains supporting evidence supplied for review."
                };

            AppendPackageGuideEntry(
                body,
                AppendixHeading(appendix),
                description);
        }


    }

    private static void AppendPackageGuideEntry(
        Body body,
        string title,
        string description)
    {
        body.Append(
            StyledParagraph(
                title,
                "Heading2"));

        body.Append(
            new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        After = "240"
                    },
                    new KeepLines()),
                new Run(
                    new RunProperties(
                        new RunFonts
                        {
                            Ascii = "Cambria",
                            HighAnsi = "Cambria"
                        },
                        new FontSize
                        {
                            Val = "24"
                        }),
                    new Text(SanitizeXmlText(description))
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    })));
    }

    private static void AppendReviewScope(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var package =
            details.PackageDetails.Package;

        var sourceCount =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence).Count;

        body.Append(
            StyledParagraph(
                "Issues Presented for Medical Review",
                "Heading1"));

        body.Append(
            ContentParagraph(
                $"Review purpose: {package.Purpose}"));

        body.Append(
            ContentParagraph(
                $"Reviewer role: {ReviewerRoleDisplayName(package.ReviewerRole)}"));

        body.Append(
            ContentParagraph(
                $"Evidence sources supplied for review: {sourceCount}."));

        body.Append(
            ContentParagraph(
                "This report organizes evidence for independent medical review. " +
                "It does not make a medical, legal, or adjudicative conclusion."));
    }

    private static void AppendMedicationProgressions(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var progressions =
            details.MedicationProgressions
                .OrderBy(
                    item => item.MedicationName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (progressions.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Relevant Medication Progression / History",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "This section highlights meaningful prescription changes for medications " +
                "identified as relevant to the medical opinion request. It preserves the " +
                "VA medication-ledger history and does not infer a clinical reason for a " +
                "change unless that reason is separately documented in the medical record. " +
                "Documented clinical context is shown only when explicitly linked to the " +
                "specific prescription record."));

        foreach (var progression in progressions)
        {
            body.Append(
                StyledParagraph(
                    progression.MedicationName,
                    "Heading2"));

            foreach (var entry in progression.Entries)
            {
                var eventDate =
                    entry.PrescribedDate ??
                    entry.LastFilledDate;

                var summary = new List<string>();

                if (eventDate is not null)
                    summary.Add(eventDate.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture));

                summary.Add(MedicationLedgerStatusDisplayName(entry.Status));

                if (!string.IsNullOrWhiteSpace(entry.Strength))
                    summary.Add(entry.Strength.Trim());

                body.Append(
                    ContentParagraph(
                        string.Join(" — ", summary)));

                if (!string.IsNullOrWhiteSpace(entry.Directions))
                {
                    body.Append(
                        ContentParagraph(
                            $"Directions: {entry.Directions.Trim()}"));
                }

                if (!string.IsNullOrWhiteSpace(entry.PrescriptionNumber))
                {
                    var contexts =
                        details.MedicationClinicalContexts
                            .Where(context =>
                                string.Equals(
                                    context.PrescriptionNumber,
                                    entry.PrescriptionNumber,
                                    StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                    foreach (var context in contexts)
                    {
                        body.Append(
                            ContentParagraph(
                                $"Documented clinical context: {context.Summary}"));
                        body.Append(
                            ContentParagraph(
                                $"Source: {context.SourceLocator}"));
                    }
                }
            }
        }
    }

    private static void AppendPrescribedMedications(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var medications =
            details.CurrentMedications
                .OrderBy(
                    item => item.MedicationName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.EntryOrdinal)
                .ToArray();

        if (medications.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Current Medication List",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "This complete current prescription snapshot is drawn from the " +
                "newest complete VA medication ledger supplied in the record. " +
                "It is not limited to medications considered relevant to the " +
                "claimed condition."));

        foreach (var medication in medications)
        {
            body.Append(
                StyledParagraph(
                    medication.MedicationName,
                    "Heading2"));

            if (!string.IsNullOrWhiteSpace(medication.Strength))
                body.Append(ContentParagraph(
                    $"Strength: {medication.Strength.Trim()}"));

            if (!string.IsNullOrWhiteSpace(medication.Directions))
                body.Append(ContentParagraph(
                    $"Directions: {medication.Directions.Trim()}"));

            if (!string.IsNullOrWhiteSpace(medication.Indication) &&
                !string.Equals(
                    medication.Indication.Trim(),
                    "None recorded",
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(ContentParagraph(
                    $"Documented indication: {medication.Indication.Trim()}"));
            }

            body.Append(
                ContentParagraph(
                    $"Current status: {MedicationLedgerStatusDisplayName(medication.Status)}"));
        }
    }

    private static string MedicationLedgerStatusDisplayName(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "active" => "Active",
            "refillinprocess" => "Refill in process",
            "transferred" => "Transferred",
            "discontinued" => "Discontinued",
            "expired" => "Expired",
            _ => status.Trim()
        };


    private static void AppendKeyEvidenceAndChronology(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var contents =
            GetRoleContents(
                    details,
                    EvidencePackageContentRoles.UnderlyingEvidence)
                .Where(
                    content =>
                        string.Equals(
                            content.Appendix,
                            VeteransReviewerPackageAppendix.MedicalEvidence,
                            StringComparison.Ordinal))
                .ToArray();

        if (contents.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Key Evidence and Chronology",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The chronology below uses evidence dates explicitly preserved " +
                "in artifact metadata. Medical sources without an indexed evidence " +
                "date remain listed after the dated entries and in the applicable appendix."));

        foreach (var content in
            contents
                .Where(
                    content =>
                        !string.IsNullOrWhiteSpace(
                            GetEvidenceDate(content)))
                .OrderBy(
                    content =>
                        GetEvidenceDate(content),
                    StringComparer.Ordinal)
                .ThenBy(
                    content =>
                        GetDisplayName(content),
                    StringComparer.OrdinalIgnoreCase))
        {
            body.Append(
                StyledParagraph(
                    $"{GetEvidenceDate(content)} — {GetDisplayName(content)}",
                    "Heading2"));

            var pages =
                GetSourcePageReference(content);

            if (!string.IsNullOrWhiteSpace(pages))
            {
                body.Append(
                    ContentParagraph(
                        $"{AppendixHeading(content.Appendix!)} | {pages}"));
            }
        }

        var undated =
            contents
                .Where(
                    content =>
                        string.IsNullOrWhiteSpace(
                            GetEvidenceDate(content)))
                .OrderBy(
                    content =>
                        GetDisplayName(content),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (undated.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Additional Medical Evidence",
                "Heading2"));

        foreach (var content in undated)
        {
            var reference =
                GetSourcePageReference(content);

            body.Append(
                ContentParagraph(
                    string.IsNullOrWhiteSpace(reference)
                        ? GetDisplayName(content)
                        : $"{GetDisplayName(content)} | {reference}"));
        }
    }

    private static void AppendMedicalLiteratureConsidered(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var contents =
            GetRoleContents(
                    details,
                    EvidencePackageContentRoles.UnderlyingEvidence)
                .Where(
                    content =>
                        string.Equals(
                            content.Appendix,
                            VeteransReviewerPackageAppendix.MedicalLiterature,
                            StringComparison.Ordinal))
                .OrderBy(
                    content =>
                        GetDisplayName(content),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (contents.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Medical / Scientific Literature Considered",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The following literature is included for the reviewing physician. " +
                "Complete source material is preserved in Appendix E."));

        foreach (var content in contents)
        {
            var displayName =
                GetDisplayName(content);

            body.Append(
                StyledParagraph(
                    displayName,
                    "Heading2"));

            var sourceName = GetSourceName(content);

            if (!string.IsNullOrWhiteSpace(sourceName) &&
                !string.Equals(
                    displayName,
                    sourceName,
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(
                    ContentParagraph(
                        $"Source: {sourceName}"));
            }

            foreach (var reviewed in
                content.ReviewedMedicalLiteratureClassifications)
            {
                if (reviewed.ArtifactId != content.Artifact.Id)
                {
                    throw new InvalidOperationException(
                        "Reviewed medical literature artifact identity mismatch.");
                }

                if (reviewed.SourceExcerpts.Any(
                        excerpt =>
                            excerpt.ArtifactId != content.Artifact.Id))
                {
                    throw new InvalidOperationException(
                        "Reviewed medical literature excerpt artifact " +
                        "identity mismatch.");
                }

                body.Append(
                    ContentParagraph(
                        $"Role: {GuidanceRoleDisplayName(reviewed.Association.GuidanceRole)}"));

                body.Append(
                    ContentParagraph(
                        $"Relevance: {reviewed.Association.Description}"));

                body.Append(
                    ContentParagraph(
                        $"Reviewed by: {reviewed.ReviewedBy}"));

                body.Append(
                    ContentParagraph(
                        $"Reviewed UTC: {reviewed.ReviewedUtc:yyyy-MM-dd HH:mm:ss} UTC"));

                foreach (var excerpt in reviewed.SourceExcerpts)
                {
                    body.Append(
                        ContentParagraph(
                            "Accepted Source Excerpt:",
                            keepWithNext: true));

                    AppendReviewerText(
                        body,
                        excerpt.Text);
                }
            }
        }
    }

    private static void AppendReviewerQuestions(
        Body body)
    {
        body.Append(
            StyledParagraph(
                "Questions for the Reviewing Physician",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "Please address the questions that are medically applicable to " +
                "the requested review and explain the rationale for each opinion."));

        body.Append(
            ContentParagraph(
                "1. What current diagnosis or diagnoses and clinically significant " +
                "findings are supported by the supplied evidence?"));

        body.Append(
            ContentParagraph(
                "2. What medical relationship, if any, is supported or not supported " +
                "by the evidence for the claim issue under review?"));

        body.Append(
            ContentParagraph(
                "3. If causation and aggravation are medically applicable to the " +
                "requested review, address each separately."));

        body.Append(
            ContentParagraph(
                "4. Identify the specific records and medical or scientific literature " +
                "relied upon, explain the medical rationale, and discuss material " +
                "evidence that weighs against the opinion."));
    }

    private static void AppendEvidenceAppendices(
        MainDocumentPart mainPart,
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var contents =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence);

        if (contents.Count == 0)
            return;

        foreach (var group in
            contents
                .Where(content => content.Appendix is not null)
                .GroupBy(content => content.Appendix!)
                .OrderBy(group => AppendixOrder(group.Key)))
        {
            body.Append(
                StyledParagraph(
                    AppendixHeading(group.Key),
                    "Heading1",
                    pageBreakBefore: true));

            var firstArtifact = true;

            foreach (var content in
                group
                    .OrderBy(
                        content =>
                            string.IsNullOrWhiteSpace(GetEvidenceDate(content))
                                ? 1
                                : 0)
                    .ThenBy(content => GetEvidenceDate(content), StringComparer.Ordinal)
                    .ThenBy(
                        content => GetDisplayName(content),
                        StringComparer.OrdinalIgnoreCase))
            {
                AppendSourceContent(
                    mainPart,
                    body,
                    details,
                    content,
                    pageBreakBefore: !firstArtifact);

                firstArtifact = false;
            }
        }

        var additionalEvidence =
            contents
                .Where(content => content.Appendix is null)
                .OrderBy(
                    content =>
                        string.IsNullOrWhiteSpace(GetEvidenceDate(content))
                            ? 1
                            : 0)
                .ThenBy(content => GetEvidenceDate(content), StringComparer.Ordinal)
                .ThenBy(
                    content => GetDisplayName(content),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (additionalEvidence.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Additional Evidence",
                "Heading1",
                pageBreakBefore: true));

        var firstAdditionalArtifact = true;

        foreach (var content in additionalEvidence)
        {
            AppendSourceContent(
                mainPart,
                body,
                details,
                content,
                pageBreakBefore: !firstAdditionalArtifact);

            firstAdditionalArtifact = false;
        }
    }

    private static void AppendSourceContent(
        MainDocumentPart mainPart,
        Body body,
        VeteransReviewerPackageDetails details,
        VeteransReviewerArtifactContent content,
        bool pageBreakBefore = false)
    {
        var displayName =
            GetDisplayName(content);

        body.Append(
            StyledParagraph(
                displayName,
                "Heading2",
                pageBreakBefore: pageBreakBefore));

        var sourceReference =
            BuildSourceReference(content);

        if (!string.IsNullOrWhiteSpace(sourceReference))
        {
            body.Append(
                ContentParagraph(
                    sourceReference,
                    keepWithNext: true));
        }

        var sourceName = GetSourceName(content);

        if (!string.IsNullOrWhiteSpace(sourceName) &&
            !string.Equals(
                displayName,
                sourceName,
                StringComparison.OrdinalIgnoreCase))
        {
            body.Append(
                ContentParagraph(
                    $"Source: {sourceName}",
                    keepWithNext: true));
        }

        var clarifications =
            details.SourceClarifications
                .Where(
                    clarification =>
                        clarification.ReviewerArtifactId == content.Artifact.Id)
                .OrderBy(
                    clarification =>
                        clarification.SourceLocator,
                    StringComparer.Ordinal)
                .ThenBy(
                    clarification =>
                        clarification.OriginalText,
                    StringComparer.Ordinal)
                .ToArray();

        AppendSourceClarifications(
            body,
            clarifications);

        if (content.PrintablePages.Count > 0)
        {
            AppendPrintablePages(
                mainPart,
                body,
                content.PrintablePages,
                displayName,
                reviewerPageSelectionApplied:
                    content.ReviewerPageSelection is not null);
            return;
        }

        if (!string.IsNullOrWhiteSpace(content.Text))
        {
            if (string.Equals(
                    content.Appendix,
                    VeteransReviewerPackageAppendix.MedicalLiterature,
                    StringComparison.Ordinal))
            {
                AppendMedicalLiteratureText(
                    body,
                    content.Text);
            }
            else
            {
                AppendReviewerText(
                    body,
                    content.Text);
            }
        }
    }

    private static void AppendSourceClarifications(
        Body body,
        IReadOnlyList<VeteransReviewerSourceClarification> clarifications)
    {
        if (clarifications.Count == 0)
            return;

        body.Append(
            StyledParagraph(
                clarifications.Count == 1
                    ? "Source Clarification"
                    : "Source Clarifications",
                "Heading3"));

        foreach (var clarification in clarifications)
        {
            body.Append(
                ContentParagraph(
                    $"Record: {clarification.SourceLocator}",
                    keepWithNext: true));

            body.Append(
                ContentParagraph(
                    $"Source text: {clarification.OriginalText}",
                    keepWithNext: true));

            body.Append(
                ContentParagraph(
                    clarification.Clarification));
        }
    }

    private static string BuildLiteratureCitation(
        VeteransReviewerArtifactContent content)
    {
        if (!string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.MedicalLiterature,
                StringComparison.Ordinal))
            return string.Empty;

        var metadata = content.Artifact.Metadata;
        var authors = GetMetadataText(
            metadata,
            EMF.Extensions.VeteransClaims.Models
                .VeteransArtifactMetadataKeys.LiteratureAuthors);
        var publication = GetMetadataText(
            metadata,
            EMF.Extensions.VeteransClaims.Models
                .VeteransArtifactMetadataKeys.LiteraturePublication);
        var doi = GetMetadataText(
            metadata,
            EMF.Extensions.VeteransClaims.Models
                .VeteransArtifactMetadataKeys.LiteratureDoi);
        var pmid = GetMetadataText(
            metadata,
            EMF.Extensions.VeteransClaims.Models
                .VeteransArtifactMetadataKeys.LiteraturePmid);

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(authors))
            parts.Add(authors);

        parts.Add(GetDisplayName(content));

        if (!string.IsNullOrWhiteSpace(publication))
            parts.Add(publication);

        var year = GetEvidenceDate(content);
        if (!string.IsNullOrWhiteSpace(year))
            parts.Add(year);

        if (!string.IsNullOrWhiteSpace(doi))
            parts.Add($"DOI: {doi}");

        if (!string.IsNullOrWhiteSpace(pmid))
            parts.Add($"PMID: {pmid}");

        return string.Join(". ", parts);
    }

    private static string ReviewerRoleDisplayName(string role) =>
        role switch
        {
            "MedicalProfessional" => "Medical Professional",
            _ => role
        };

    private static string GuidanceRoleDisplayName(string role) =>
        role switch
        {
            EvidenceGuidanceRoles.SupportsRequirement =>
                "Supports Requirement",
            EvidenceGuidanceRoles.EstablishesElement =>
                "Establishes Element",
            EvidenceGuidanceRoles.Corroborates =>
                "Corroborates",
            EvidenceGuidanceRoles.Clarifies =>
                "Clarifies",
            _ => role
        };

    private static string? GetSourceName(
        VeteransReviewerArtifactContent content)
    {
        if (VeteransReviewerDisplayNameResolver
            .IsReviewerFacingLabel(content.SourceName))
        {
            return content.SourceName!.Trim();
        }

        if (VeteransReviewerDisplayNameResolver
            .IsReviewerFacingLabel(content.Artifact.Name))
        {
            return content.Artifact.Name.Trim();
        }

        return null;
    }

    private static string GetDisplayName(
        VeteransReviewerArtifactContent content)
    {
        var evidenceTitle =
            GetMetadataText(
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.EvidenceTitle);

        if (!string.IsNullOrWhiteSpace(evidenceTitle))
            return evidenceTitle;

        var noteTitle =
            GetMetadataText(
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.NoteTitle);

        if (!string.IsNullOrWhiteSpace(noteTitle))
            return noteTitle;

        if (string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.MedicalLiterature,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(content.Text))
        {
            var firstLine =
                NormalizeReviewerText(content.Text)
                    .FirstOrDefault(line => line.Length > 0);

            if (!string.IsNullOrWhiteSpace(firstLine) &&
                firstLine.Length <= 180)
            {
                return firstLine;
            }
        }

        var derivedDisplayName =
            GetReviewerDerivedDisplayName(content);

        if (!string.IsNullOrWhiteSpace(derivedDisplayName))
            return derivedDisplayName;

        return VeteransReviewerDisplayNameResolver.Resolve(
            GetReviewerFallbackDisplayName(content),
            content.SourceName,
            content.Artifact.Name);
    }

    private static string? GetReviewerDerivedDisplayName(
        VeteransReviewerArtifactContent content)
    {
        var artifactName = content.Artifact.Name;
        var text = content.Text;

        if (ContainsReviewerText(artifactName, "Blue-Button") ||
            ContainsReviewerText(artifactName, "Blue Button"))
        {
            return "VA Blue Button Report";
        }

        if (string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.MedicalEvidence,
                StringComparison.Ordinal))
        {
            if (ContainsReviewerText(artifactName, "OSCAR"))
                return "OSCAR PAP Therapy Data";

            if (ContainsReviewerText(artifactName, "Jupiter") &&
                ContainsReviewerText(artifactName, "CPAP-Titration"))
            {
                return "CPAP Titration Study — Jupiter Medical Center";
            }

            if ((ContainsReviewerText(text, "AirCurve 11 ASV") ||
                 ContainsReviewerText(text, "AirCurve11ASV")) &&
                (ContainsReviewerText(text, "AirView") ||
                 ContainsReviewerText(text, "Therapy Report")))
            {
                return "ResMed AirView Therapy Report — AirCurve 11 ASV";
            }

            if (ContainsReviewerText(text, "OSCAR"))
                return "OSCAR PAP Therapy Data";

            if (ContainsReviewerText(text, "Jupiter Medical Center") &&
                (ContainsReviewerText(text, "CPAP titration") ||
                 ContainsReviewerText(text, "polysomnogram")))
            {
                return "CPAP Titration Study — Jupiter Medical Center";
            }
        }

        if (string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.LayEvidence,
                StringComparison.Ordinal))
        {
            if (ContainsReviewerText(artifactName, "spousal"))
                return "Spousal Statement";

            if (ContainsReviewerText(artifactName, "personal"))
                return "Veteran Personal Statement";
        }

        if (string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.AdjudicativeRecords,
                StringComparison.Ordinal) &&
            (ContainsReviewerText(artifactName, "ClaimLetter") ||
             ContainsReviewerText(text, "Rating Decision")))
        {
            return "VA Claim Decision Letter";
        }

        return null;
    }

    private static bool ContainsReviewerText(
        string? value,
        string expected) =>
        value?.Contains(
            expected,
            StringComparison.OrdinalIgnoreCase) == true;

    private static string GetReviewerFallbackDisplayName(
        VeteransReviewerArtifactContent content) =>
        content.Appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "Medical Evidence",
            VeteransReviewerPackageAppendix.ServiceRecords =>
                "Service Record",
            VeteransReviewerPackageAppendix.LayEvidence =>
                "Lay Evidence",
            VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                "Adjudicative Record",
            VeteransReviewerPackageAppendix.MedicalLiterature =>
                "Medical / Scientific Literature",
            _ => "Evidence of Record"
        };

    private static string BuildSourceReference(
        VeteransReviewerArtifactContent content)
    {
        var parts = new List<string>();
        var date = GetEvidenceDate(content);

        if (!string.IsNullOrWhiteSpace(date))
            parts.Add($"Date: {date}");

        var pages = GetSourcePageReference(content);

        if (!string.IsNullOrWhiteSpace(pages))
            parts.Add(pages);

        return string.Join(" | ", parts);
    }

    private static string GetEvidenceDate(
        VeteransReviewerArtifactContent content)
    {
        var evidenceDate =
            GetMetadataText(
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.EvidenceDate);

        if (!string.IsNullOrWhiteSpace(evidenceDate))
            return evidenceDate;

        return GetMetadataText(
            content.Artifact.Metadata,
            EMF.Extensions.VeteransClaims.Models
                .VeteransArtifactMetadataKeys.NoteDate) ?? string.Empty;
    }

    private static string GetSourcePageReference(
        VeteransReviewerArtifactContent content)
    {
        if (content.Relationships.Any(
                relationship =>
                    relationship.SourceArtifactId == content.Artifact.Id &&
                    string.Equals(
                        relationship.RelationshipType,
                        EMF.Core.Models.RelationshipTypes.DerivedFrom,
                        StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        var start =
            GetMetadataText(
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceStartPage);

        var end =
            GetMetadataText(
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceEndPage);

        if (string.IsNullOrWhiteSpace(start))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(end) ||
            string.Equals(start, end, StringComparison.Ordinal))
        {
            return $"Source page: {start}";
        }

        return $"Source pages: {start}-{end}";
    }

    private static string? GetMetadataText(
        IReadOnlyDictionary<string, object> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var value))
            return null;

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text)
            ? null
            : text;
    }

    private static int AppendixOrder(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence => 0,
            VeteransReviewerPackageAppendix.ServiceRecords => 1,
            VeteransReviewerPackageAppendix.LayEvidence => 2,
            VeteransReviewerPackageAppendix.AdjudicativeRecords => 3,
            VeteransReviewerPackageAppendix.MedicalLiterature => 4,
            _ => int.MaxValue
        };

    private static string AppendixHeading(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "Appendix A — Medical Evidence",
            VeteransReviewerPackageAppendix.ServiceRecords =>
                "Appendix B — Service Records",
            VeteransReviewerPackageAppendix.LayEvidence =>
                "Appendix C — Lay Evidence",
            VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                "Appendix D — Adjudicative Records",
            VeteransReviewerPackageAppendix.MedicalLiterature =>
                "Appendix E — Medical / Scientific Literature",
            _ => appendix
        };

    private static Footer ReviewerFooter()
    {
        var table =
            new Table(
                new TableProperties(
                    new TableWidth
                    {
                        Type = TableWidthUnitValues.Pct,
                        Width = "5000"
                    }),
                new TableRow(
                    FooterCell(
                        4000,
                        JustificationValues.Center,
                        noWrap: false,
                        FooterRun(
                            "CONFIDENTIAL — VETERAN MEDICAL INFORMATION",
                            bold: true),
                        FooterRun(
                            "  |  Veterans Evidence Reviewer Report")),
                    FooterCell(
                        1000,
                        JustificationValues.Right,
                        noWrap: true,
                        FooterRun("Page "),
                        FooterField("PAGE"),
                        FooterRun(" of "),
                        FooterField("NUMPAGES"))));

        return new Footer(table);
    }

    private static TableCell FooterCell(
        int widthPercentFiftieths,
        JustificationValues justification,
        bool noWrap,
        params OpenXmlElement[] contents)
    {
        var cellProperties =
            new TableCellProperties(
                new TableCellWidth
                {
                    Type = TableWidthUnitValues.Pct,
                    Width = widthPercentFiftieths.ToString(
                        CultureInfo.InvariantCulture)
                });

        if (noWrap)
            cellProperties.Append(new NoWrap());

        var paragraph =
            new Paragraph(
                new ParagraphProperties(
                    new Justification
                    {
                        Val = justification
                    },
                    new SpacingBetweenLines
                    {
                        Before = "120"
                    }));

        paragraph.Append(contents);

        return new TableCell(
            cellProperties,
            paragraph);
    }

    private static Run FooterRun(
        string text,
        bool bold = false)
    {
        var properties =
            FooterRunProperties(bold);

        return new Run(
            properties,
            new Text(SanitizeXmlText(text))
            {
                Space = SpaceProcessingModeValues.Preserve
            });
    }

    private static SimpleField FooterField(string instruction)
    {
        var field =
            new SimpleField
            {
                Instruction = instruction,
                Dirty = true
            };

        field.Append(
            new Run(
                FooterRunProperties(),
                new Text("1")));

        return field;
    }

    private static RunProperties FooterRunProperties(
        bool bold = false)
    {
        var properties =
            new RunProperties(
                new RunFonts
                {
                    Ascii = "Cambria",
                    HighAnsi = "Cambria"
                });

        if (bold)
            properties.Append(new Bold());

        properties.Append(
            new Color
            {
                Val = "666666"
            },
            new FontSize
            {
                Val = "20"
            });

        return properties;
    }

    private static Paragraph ConfidentialParagraph()
    {
        var properties =
            new ParagraphProperties(
                new Justification
                {
                    Val = JustificationValues.Center
                },
                new SpacingBetweenLines
                {
                    Before = "240",
                    After = "240"
                });

        return new Paragraph(
            properties,
            new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = "Cambria",
                        HighAnsi = "Cambria"
                    },
                    new Bold(),
                    new FontSize
                    {
                        Val = "36"
                    }),
                new Text(
                    "CONFIDENTIAL — VETERAN MEDICAL INFORMATION")));
    }

    private static Paragraph StyledParagraph(
        string text,
        string styleId,
        bool pageBreakBefore = false)
    {
        var properties =
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = styleId
                });

        if (pageBreakBefore)
            properties.Append(new PageBreakBefore());

        if (string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal))
        {
            properties.Append(
                new Justification
                {
                    Val = JustificationValues.Center
                });

            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "720",
                    After = "360"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading1",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new KeepNext());

            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "180",
                    After = "120"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading2",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new KeepNext());

            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "60"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Subtitle",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new Justification
                {
                    Val = JustificationValues.Center
                });

            properties.Append(
                new SpacingBetweenLines
                {
                    After = "60"
                });
        }

        return new Paragraph(
            properties,
            new Run(
                ReviewerRunProperties(styleId),
                new Text(SanitizeXmlText(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    private static RunProperties ReviewerRunProperties(
        string styleId)
    {
        var properties =
            new RunProperties(
                new RunFonts
                {
                    Ascii = "Cambria",
                    HighAnsi = "Cambria"
                });

        if (string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "1F4E79" });
            properties.Append(new FontSize { Val = "40" });
        }
        else if (string.Equals(
                     styleId,
                     "Heading1",
                     StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "1F4E79" });
            properties.Append(new FontSize { Val = "32" });
        }
        else if (string.Equals(
                     styleId,
                     "Heading2",
                     StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "365F91" });
            properties.Append(new FontSize { Val = "26" });
        }
        else if (string.Equals(
                     styleId,
                     "Subtitle",
                     StringComparison.Ordinal))
        {
            properties.Append(new Color { Val = "666666" });
            properties.Append(new FontSize { Val = "24" });
        }

        return properties;
    }

    private static Paragraph PageBreakParagraph() =>
        new(
            new Run(
                new Break
                {
                    Type = BreakValues.Page
                }));

    private static void AppendMetadata(
        Body body,
        IReadOnlyDictionary<string, object> metadata,
        string key,
        string label)
    {
        if (!metadata.TryGetValue(key, out var value))
            return;

        var text = value?.ToString();

        if (string.IsNullOrWhiteSpace(text))
            return;

        body.Append(
            ContentParagraph(
                $"{label}: {text}"));
    }

    private static Paragraph ContentParagraph(
        string text,
        bool keepWithNext = false)
    {
        var properties =
            new ParagraphProperties(
                new SpacingBetweenLines
                {
                    After = "60"
                });

        properties.Append(new KeepLines());

        if (keepWithNext)
            properties.Append(new KeepNext());

        return new Paragraph(
            properties,
            new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = "Cambria",
                        HighAnsi = "Cambria"
                    },
                    new FontSize
                    {
                        Val = "24"
                    }),
                new Text(SanitizeXmlText(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    private static void AppendReviewerText(
        Body body,
        string text)
    {
        foreach (var line in NormalizeReviewerText(text))
        {
            if (line.Length == 0)
            {
                body.Append(ContentParagraph(string.Empty));
                continue;
            }

            var properties =
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        After =
                            IsReviewerStructuralLine(line)
                                ? "40"
                                : "60"
                    });

            properties.Append(new KeepLines());

            if (IsReviewerHeadingLine(line))
                properties.Append(new KeepNext());

            body.Append(
                new Paragraph(
                    properties,
                    new Run(
                        new RunProperties(
                            new RunFonts
                            {
                                Ascii = "Cambria",
                                HighAnsi = "Cambria"
                            },
                            new FontSize
                            {
                                Val = "24"
                            }),
                        new Text(SanitizeXmlText(line))
                        {
                            Space =
                                SpaceProcessingModeValues.Preserve
                        })));
        }
    }

    private static void AppendMedicalLiteratureText(
        Body body,
        string text)
    {
        foreach (var line in NormalizeReviewerText(text))
        {
            if (line.Length == 0)
                continue;

            var heading =
                IsMedicalLiteratureHeadingLine(line);

            var properties =
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        Before = heading ? "160" : "0",
                        After = heading ? "80" : "120",
                        Line = heading ? "240" : "276",
                        LineRule = LineSpacingRuleValues.Auto
                    });

            properties.Append(new KeepLines());

            if (heading)
                properties.Append(new KeepNext());

            var runProperties =
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = "Cambria",
                        HighAnsi = "Cambria"
                    },
                    new FontSize
                    {
                        Val = "24"
                    });

            if (heading)
                runProperties.Append(new Bold());

            body.Append(
                new Paragraph(
                    properties,
                    new Run(
                        runProperties,
                        new Text(SanitizeXmlText(line))
                        {
                            Space =
                                SpaceProcessingModeValues.Preserve
                        })));
        }
    }

    private static bool IsMedicalLiteratureHeadingLine(
        string line)
    {
        var heading =
            line.Trim().TrimEnd(':');

        return heading.Equals(
                   "Abstract",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Introduction",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Background",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Methods",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Materials and Methods",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Results",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Discussion",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Conclusion",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Conclusions",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "References",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Acknowledgments",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Funding",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Disclosures",
                   StringComparison.OrdinalIgnoreCase) ||
               heading.Equals(
                   "Keywords",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> NormalizeReviewerText(
        string text)
    {
        var normalized =
            text.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace('\uFFFD', ' ');

        var output = new List<string>();
        var paragraph = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Length == 0)
                return;

            output.Add(paragraph.ToString());
            paragraph.Clear();
        }

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                FlushParagraph();

                if (output.Count > 0 &&
                    output[^1].Length != 0)
                {
                    output.Add(string.Empty);
                }

                continue;
            }

            if (IsReviewerStructuralLine(line))
            {
                FlushParagraph();
                output.Add(line);
                continue;
            }

            if (paragraph.Length > 0)
                paragraph.Append(' ');

            paragraph.Append(line);
        }

        FlushParagraph();

        while (output.Count > 0 &&
               output[^1].Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        return output;
    }

    private static bool IsReviewerStructuralLine(
        string line) =>
        IsReviewerHeadingLine(line) ||
        IsReviewerFieldLine(line) ||
        line.StartsWith("•", StringComparison.Ordinal) ||
        line.StartsWith("- ", StringComparison.Ordinal) ||
        line.StartsWith("* ", StringComparison.Ordinal) ||
        line.StartsWith("/es/", StringComparison.OrdinalIgnoreCase);

    private static bool IsReviewerHeadingLine(
        string line)
    {
        if (line.EndsWith(':') ||
            string.Equals(
                line,
                "Details",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                line,
                "Note",
                StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("***", StringComparison.Ordinal))
        {
            return true;
        }

        var letterCount = 0;

        foreach (var character in line)
        {
            if (!char.IsLetter(character))
                continue;

            letterCount++;

            if (!char.IsUpper(character))
                return false;
        }

        return letterCount >= 3 &&
               line.Length <= 100;
    }

    private static bool IsReviewerFieldLine(
        string line)
    {
        var colon = line.IndexOf(':');

        if (colon <= 0 ||
            colon > 60)
        {
            return false;
        }

        for (var index = 0;
             index < colon;
             index++)
        {
            var character = line[index];

            if (char.IsLetterOrDigit(character) ||
                char.IsWhiteSpace(character) ||
                character is '(' or ')' or '/' or '-' or '&' or '.' or '%')
            {
                continue;
            }

            return false;
        }

        return true;
    }


    private static void AppendPrintablePages(
        MainDocumentPart mainPart,
        Body body,
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage> pages,
        string displayName,
        bool reviewerPageSelectionApplied)
    {
        var previousPageNumber = 0;
        var renderedPageCount = 0;
        var reviewerPageCount = pages.Count;

        foreach (var page in pages)
        {
            if (page.PageNumber <= 0 ||
                page.PageNumber <= previousPageNumber)
            {
                throw new InvalidOperationException(
                    "Printable artifact pages must be in strictly increasing order.");
            }

            if (renderedPageCount > 0)
                body.Append(PageBreakParagraph());

            if (reviewerPageCount > 1)
            {
                body.Append(
                    ReviewerContinuationParagraph(
                        displayName,
                        renderedPageCount + 1,
                        reviewerPageCount));
            }

            if (!reviewerPageSelectionApplied &&
                page.PageNumber > previousPageNumber + 1)
            {
                var firstBlankPage = previousPageNumber + 1;
                var lastBlankPage = page.PageNumber - 1;

                var blankPageNotice =
                    firstBlankPage == lastBlankPage
                        ? $"Source Page {firstBlankPage} was blank in the original document and is intentionally omitted from this reviewer copy."
                        : $"Source Pages {firstBlankPage}-{lastBlankPage} were blank in the original document and are intentionally omitted from this reviewer copy.";

                body.Append(
                    ContentParagraph(
                        blankPageNotice,
                        keepWithNext: true));
            }

            if (string.Equals(
                    page.ContentType,
                    "text/plain",
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(
                    ContentParagraph(
                        $"Source Page {page.PageNumber}",
                        keepWithNext: true));

                AppendReviewerText(
                    body,
                    DecodePrintableText(page.Content));
                previousPageNumber = page.PageNumber;
            renderedPageCount++;
                continue;
            }

            if (!string.Equals(
                    page.ContentType,
                    "image/png",
                    StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    $"Unsupported printable page content type '{page.ContentType}'.");

            var (width, height) =
                GetPngDimensions(page.Content);

            var imagePart =
                mainPart.AddImagePart(
                    ImagePartType.Png);

            using (var stream =
                new MemoryStream(
                    page.Content.ToArray(),
                    writable: false))
            {
                imagePart.FeedData(stream);
            }

            var relationshipId =
                mainPart.GetIdOfPart(imagePart);

            var (cx, cy) =
                FitPageToDocument(
                    width,
                    height,
                    reserveSourceHeadingSpace:
                        renderedPageCount == 0 ||
                        reviewerPageCount > 1);

            var drawingId =
                checked((uint)mainPart.ImageParts.Count());

            body.Append(
                ContentParagraph(
                    $"Source Page {page.PageNumber}",
                    keepWithNext: true));

            body.Append(
                ImageParagraph(
                    relationshipId,
                    drawingId,
                    page.PageNumber,
                    cx,
                    cy));

            previousPageNumber = page.PageNumber;
            renderedPageCount++;
        }
    }

    private static Paragraph ReviewerContinuationParagraph(
        string displayName,
        int reviewerPageNumber,
        int reviewerPageCount)
    {
        if (reviewerPageNumber <= 0 ||
            reviewerPageNumber > reviewerPageCount ||
            reviewerPageCount <= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reviewerPageNumber));
        }

        var suffix =
            reviewerPageNumber == 1
                ? string.Empty
                : " — Continued";

        return new Paragraph(
            new ParagraphProperties(
                new Justification
                {
                    Val = JustificationValues.Right
                },
                new KeepNext(),
                new SpacingBetweenLines
                {
                    Before = "0",
                    After = "40"
                }),
            new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = "Cambria",
                        HighAnsi = "Cambria"
                    },
                    new Italic(),
                    new Color
                    {
                        Val = "666666"
                    },
                    new FontSize
                    {
                        Val = "18"
                    }),
                new Text(
                    SanitizeXmlText(
                        $"{displayName} — {reviewerPageNumber} of " +
                        $"{reviewerPageCount}{suffix}"))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    private static string DecodePrintableText(ReadOnlyMemory<byte> content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content.Span).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "Printable text content is not valid UTF-8.", ex);
        }
    }

    private static (uint Width, uint Height) GetPngDimensions(
        ReadOnlyMemory<byte> content)
    {
        ReadOnlySpan<byte> pngHeader =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];

        if (content.Length < 24 ||
            !content.Span[..8].SequenceEqual(pngHeader))
            throw new InvalidDataException(
                "Printable page is not a valid PNG image.");

        var width =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Span.Slice(16, 4));

        var height =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Span.Slice(20, 4));

        if (width == 0 || height == 0)
            throw new InvalidDataException(
                "Printable PNG page has invalid dimensions.");

        return (width, height);
    }

    private static (long Cx, long Cy) FitPageToDocument(
        uint width,
        uint height,
        bool reserveSourceHeadingSpace)
    {
        const long maxWidth = 5_943_600;
        const long standardMaxHeight = 7_772_400;
        const long firstSourcePageMaxHeight = 6_400_800;

        var maxHeight =
            reserveSourceHeadingSpace
                ? firstSourcePageMaxHeight
                : standardMaxHeight;

        var scale =
            Math.Min(
                maxWidth / (double)width,
                maxHeight / (double)height);

        return (
            checked((long)Math.Round(width * scale)),
            checked((long)Math.Round(height * scale)));
    }

    private static Paragraph ImageParagraph(
        string relationshipId,
        uint drawingId,
        int pageNumber,
        long cx,
        long cy) =>
        new(
            new ParagraphProperties(
                new Justification
                {
                    Val = JustificationValues.Center
                }),
            new Run(
                new Drawing(
                    new DW.Inline(
                        new DW.Extent
                        {
                            Cx = cx,
                            Cy = cy
                        },
                        new DW.DocProperties
                        {
                            Id = drawingId,
                            Name = $"Source Page {pageNumber}"
                        },
                        new DW.NonVisualGraphicFrameDrawingProperties(
                            new A.GraphicFrameLocks
                            {
                                NoChangeAspect = true
                            }),
                        new A.Graphic(
                            new A.GraphicData(
                                new PIC.Picture(
                                    new PIC.NonVisualPictureProperties(
                                        new PIC.NonVisualDrawingProperties
                                        {
                                            Id = 0U,
                                            Name = $"Source Page {pageNumber}.png"
                                        },
                                        new PIC.NonVisualPictureDrawingProperties()),
                                    new PIC.BlipFill(
                                        new A.Blip
                                        {
                                            Embed = relationshipId
                                        },
                                        new A.Stretch(
                                            new A.FillRectangle())),
                                    new PIC.ShapeProperties(
                                        new A.Transform2D(
                                            new A.Offset
                                            {
                                                X = 0L,
                                                Y = 0L
                                            },
                                            new A.Extents
                                            {
                                                Cx = cx,
                                                Cy = cy
                                            }),
                                        new A.PresetGeometry(
                                            new A.AdjustValueList())
                                        {
                                            Preset =
                                                A.ShapeTypeValues.Rectangle
                                        })))
                            {
                                Uri =
                                    "http://schemas.openxmlformats.org/" +
                                    "drawingml/2006/picture"
                            })))));

    private static string SanitizeXmlText(string text)
    {
        text =
            VeteransReviewerPackagePrivacySanitizer.Redact(text);

        var sanitized = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;

            if (value == 0xFFFD)
            {
                sanitized.Append(' ');
            }
            else if (value is 0x9 or 0xA or 0xD ||
                     value is >= 0x20 and <= 0xD7FF ||
                     value is >= 0xE000 and <= 0xFFFD ||
                     value is >= 0x10000 and <= 0x10FFFF)
            {
                sanitized.Append(rune.ToString());
            }
            else
            {
                sanitized.Append(' ');
            }
        }

        return sanitized.ToString();
    }

    private static Paragraph Paragraph(
        string text) =>
        new(
            new Run(
                new Text(SanitizeXmlText(text))
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
}

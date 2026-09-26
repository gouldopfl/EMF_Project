using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Clinical;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPackageDocxRenderer
{
    private const string CurrentMedicationSectionTitle =
        "Current Medication Use — Reconciled";

    private const string MedicationProgressionSectionTitle =
        "Relevant Medications for Medical Opinion";

    private const string MedicalLiteratureSectionTitle =
        "Medical / Scientific Literature Considered";

    private sealed record PackageGuideSection(
        string Title,
        string Description);

    public static byte[] Render(
        VeteransReviewerPackageDetails details,
        IReadOnlyList<VeteransReviewerApplicableRegulation>? applicableRegulations = null,
        DateOnly? sourceReviewDate = null)
    {
        ArgumentNullException.ThrowIfNull(details);

        var regulations =
            applicableRegulations ??
            Array.Empty<VeteransReviewerApplicableRegulation>();

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

            var stylesPart =
                mainPart.AddNewPart<StyleDefinitionsPart>();

            stylesPart.Styles =
                ReviewerStyles();

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

            VeteransReviewerFonts.Embed(mainPart);

            var body =
                new Body(
                    ConfidentialParagraph(),
                    StyledParagraph(
                        "Veterans Evidence Package for Medical Review",
                        "Title"));

            if (!string.IsNullOrWhiteSpace(details.VeteranDisplayName))
            {
                body.Append(
                    StyledParagraph(
                        $"Veteran: {details.VeteranDisplayName}",
                        "Subtitle"));
            }

            body.Append(
                StyledParagraph(
                    $"Purpose: {package.Purpose}",
                    "Subtitle"),
                StyledParagraph(
                    $"Reviewer Role: {ReviewerRoleDisplayName(package.ReviewerRole)}",
                    "Subtitle"));

            if (!string.IsNullOrWhiteSpace(details.PackagePreparedBy))
            {
                body.Append(
                    StyledParagraph(
                        $"Package Prepared By: {details.PackagePreparedBy}",
                        "Subtitle"));
            }

            body.Append(
                StyledParagraph(
                    "Prepared Using: EMF Veterans Evidence System",
                    "Subtitle"));

            if (details.MedicalOpinionRequested is not null)
            {
                body.Append(ReviewerSubsectionHeading("Medical Opinion Requested"));
                body.Append(ContentParagraph(details.MedicalOpinionRequested.OpinionText));
            }

            var evidenceSections = new VeteransReviewerEvidenceSections(mainPart, footerRelationshipId);
            AppendGeneratedSection(body, evidenceSections, "Reviewer Instructions",
                section => AppendReviewerInstructions(section, details));
            if (regulations.Count > 0)
                AppendGeneratedSection(body, evidenceSections, "Applicable VA Regulation",
                    section => AppendApplicableRegulations(section, regulations));
            AppendGeneratedSection(body, evidenceSections, "Executive Summary",
                section => AppendExecutiveSummary(section, details));
            AppendGeneratedSection(body, evidenceSections, "Package Guide",
                section => AppendPackageGuide(section, details));
            AppendGeneratedSection(body, evidenceSections, "Issues Presented for Medical Review",
                section => AppendReviewScope(section, details));
            AppendGeneratedSection(body, evidenceSections, "PAP Adherence / Compliance Summary",
                section => AppendPapAdherenceSummary(section, details));
            AppendGeneratedSection(body, evidenceSections, "Sleep Study / PAP Titration Results",
                section => AppendSleepStudyPapTitrationResults(section, details));
            AppendGeneratedSection(body, evidenceSections, "Clinical Progression",
                section => AppendClinicalProgression(section, details));
            AppendGeneratedSection(body, evidenceSections, CurrentMedicationSectionTitle,
                section => AppendPrescribedMedications(section, details));
            AppendGeneratedSection(body, evidenceSections, MedicationProgressionSectionTitle,
                section => AppendMedicationProgressions(section, details));
            AppendGeneratedSection(body, evidenceSections, "Key Evidence and Chronology",
                section => AppendKeyEvidenceAndChronology(section, details));
            AppendGeneratedSection(body, evidenceSections, MedicalLiteratureSectionTitle,
                section => AppendMedicalLiteratureConsidered(section, details));
            AppendGeneratedSection(body, evidenceSections, "Questions for the Reviewing Physician",
                AppendReviewerQuestions);

            AppendEvidenceAppendices(mainPart, body, details, evidenceSections,
                sourceReviewDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
            body.Append(evidenceSections.CurrentProperties());

            mainPart.Document =
                new Document(body);
        }

        return stream.ToArray();
    }

    private static void AppendGeneratedSection(Body body, VeteransReviewerEvidenceSections sections,
        string subject, Action<Body> append)
    {
        var content = new Body();
        append(content);
        // The section break owns the page boundary; remove the old leading break.
        while (content.FirstChild is Paragraph first && first.InnerText.Length == 0 &&
               first.ParagraphProperties?.GetFirstChild<PageBreakBefore>() is not null)
            first.Remove();
        if (!content.HasChildren) return;
        sections.Start(body, subject, "1F4E79");
        foreach (var child in content.ChildElements.ToArray())
        {
            child.Remove();
            body.Append(child);
        }
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

        foreach (var progressionEvent in details.ClinicalProgressionEvents)
        {
            if (!details.ArtifactContents.Any(
                    content =>
                        content.Artifact.Id == progressionEvent.ReviewerArtifactId &&
                        packageArtifacts.Any(
                            packageArtifact =>
                                packageArtifact.ArtifactId == content.Artifact.Id &&
                                string.Equals(
                                    packageArtifact.ContentRole,
                                    EvidencePackageContentRoles.UnderlyingEvidence,
                                    StringComparison.Ordinal))))
            {
                throw new InvalidOperationException(
                    "Reviewer clinical progression event is not associated with " +
                    "underlying evidence in the package.");
            }

            if (!ClinicalProgressionEventTypes.IsSupported(
                    progressionEvent.EventType) ||
                string.IsNullOrWhiteSpace(progressionEvent.SourceLocator) ||
                string.IsNullOrWhiteSpace(progressionEvent.Summary))
            {
                throw new InvalidOperationException(
                    "Reviewer clinical progression event is incomplete.");
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

            var hasReviewerMatch =
                !string.IsNullOrWhiteSpace(clarification.ReviewerMatchText);
            var hasReviewerReplacement =
                !string.IsNullOrWhiteSpace(
                    clarification.ReviewerReplacementText);

            if (hasReviewerMatch != hasReviewerReplacement)
                throw new InvalidOperationException(
                    "Reviewer source clarification correction is incomplete.");
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

    private static Paragraph ReviewerSubsectionHeading(string text) =>
        WithReviewerSubsectionSpacing(StyledParagraph(text, "Heading2"));

    private static Paragraph WithReviewerSubsectionSpacing(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties ??= new ParagraphProperties();
        var spacing = properties.GetFirstChild<SpacingBetweenLines>();
        if (spacing is null)
            properties.Append(spacing = new SpacingBetweenLines());
        spacing.Before = "240"; // One 12-point line, without an empty content paragraph.
        return paragraph;
    }

    private static void AppendReviewerInstructions(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        body.Append(
            StyledParagraph(
                "Reviewer Instructions",
                "Heading1"));

        if (details.MedicalOpinionRequested is not null)
        {
            if (string.IsNullOrWhiteSpace(
                    details.MedicalOpinionRequested.OpinionText))
            {
                throw new InvalidOperationException(
                    "Reviewer medical opinion request text is empty.");
            }

            body.Append(
                ReviewerSubsectionHeading("Medical Opinion Requested"));

            body.Append(
                ContentParagraph(
                    details.MedicalOpinionRequested.OpinionText));
        }

        body.Append(
            ReviewerSubsectionHeading("How to Use This Package"));

        var hasApplicableRegulations =
            details.MedicalOpinionRequested?
                .ApplicableRegulatoryCitations.Count > 0;

        var howToUse =
            hasApplicableRegulations
                ? "Read the Medical Opinion Requested and the Applicable VA " +
                  "Regulation section first. Then review the Issues Presented for " +
                  "Medical Review and Questions for the Reviewing Physician. Use " +
                  "the Key Evidence and Chronology for orientation to the principal " +
                  "medical evidence, then use the appendices to review the " +
                  "underlying source material."
                : "Review the Medical Opinion Requested, Issues Presented for Medical " +
                  "Review, and Questions for the Reviewing Physician first. Use the " +
                  "Key Evidence and Chronology for orientation to the principal " +
                  "medical evidence, then use the appendices to review the " +
                  "underlying source material.";

        if (HasPackageGuideSection(
                details,
                MedicalLiteratureSectionTitle))
        {
            howToUse +=
                " Medical/scientific literature is reproduced in Appendix F.";
        }

        body.Append(
            ContentParagraph(
                howToUse));

        body.Append(
            ReviewerSubsectionHeading("Reviewer Guidance"));

        body.Append(
            ContentParagraph(
                "Base any opinion on the evidence supplied, identify the specific " +
                "records and medical/scientific literature relied upon, address " +
                "medically applicable causation and aggravation questions separately, " +
                "and explain the medical rationale for each opinion."));
    }

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

        if (details.MedicalOpinionRequested is not null)
        {
            body.Append(
                ReviewerSubsectionHeading("Medical Opinion Requested"));

            body.Append(
                ContentParagraph(
                    details.MedicalOpinionRequested.OpinionText));
        }

        if (details.MedicalOpinionRequested is not null &&
            details.MedicalOpinionRequested
                .ApplicableRegulatoryCitations.Count > 0)
        {
            body.Append(
                WithReviewerSubsectionSpacing(ContentParagraph(
                    "Applicable VA Regulation: " +
                    string.Join(
                        "; ",
                        details.MedicalOpinionRequested
                            .ApplicableRegulatoryCitations),
                    keepWithNext: true)));
        }

        body.Append(
            ReviewerSubsectionHeading("Purpose of This Document"));

        body.Append(
            ContentParagraph(
                $"Purpose: {package.Purpose}. This package organizes the evidence " +
                "supplied for independent medical review and is intended to help the " +
                "reviewing medical professional locate and evaluate the relevant " +
                "medical, lay, adjudicative, treatment-device, and medical/scientific " +
                "evidence efficiently. It does not make a medical, legal, or " +
                "adjudicative conclusion."));

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

        foreach (var section in GetPackageGuideSections(details))
        {
            AppendPackageGuideEntry(
                body,
                section.Title,
                section.Description);
        }
    }

    private static IReadOnlyList<PackageGuideSection> GetPackageGuideSections(
        VeteransReviewerPackageDetails details)
    {
        var sections =
            new List<PackageGuideSection>
            {
                new(
                    "Issues Presented for Medical Review",
                    "Defines the review purpose, reviewer role, evidence scope, and limitations.")
            };

        if (HasPapAdherenceSummary(details))
        {
            sections.Add(
                new PackageGuideSection(
                    "PAP Adherence / Compliance Summary",
                    "Provides an up-front, source-grounded view of sustained PAP use so later residual-AHI and mask/leak findings are interpreted in adherence context."));
        }

        if (GetPapTitrationSummaryItems(details).Count > 0)
        {
            sections.Add(
                new PackageGuideSection(
                    "Sleep Study / PAP Titration Results",
                    "Summarizes primary PAP titration studies supplied as evidence together with titration findings documented in provider notes, preserving longitudinal treatment context."));
        }

        if (details.ClinicalProgressionEvents.Any(
                item => !IsPapTitrationFinding(item)))
        {
            sections.Add(
                new PackageGuideSection(
                    "Clinical Progression",
                    "Summarizes source-grounded treatment use, problems, adjustments, transitions, findings, and responses relevant to the medical review."));
        }

        if (details.CurrentMedications.Count > 0)
        {
            sections.Add(
                new PackageGuideSection(
                    CurrentMedicationSectionTitle,
                    "Lists only medications explicitly confirmed as currently used through medication reconciliation; VA prescription status alone is not treated as verified current use."));
        }

        if (details.MedicationProgressions.Count > 0)
        {
            sections.Add(
                new PackageGuideSection(
                    MedicationProgressionSectionTitle,
                    "Summarizes dated VA prescription history and attributed indication reconciliations relevant to the medical opinion, grouped by service-connected condition."));
        }

        sections.Add(
            new PackageGuideSection(
                "Key Evidence and Chronology",
                "Provides chronological orientation to the principal medical evidence."));

        if (HasMedicalLiterature(details))
        {
            sections.Add(
                new PackageGuideSection(
                    MedicalLiteratureSectionTitle,
                    "Identifies literature supplied for consideration during the medical review."));
        }

        sections.Add(
            new PackageGuideSection(
                "Questions for the Reviewing Physician",
                "Lists the medical questions the reviewing physician is asked to address."));

        IReadOnlyList<string> appendices =
            GetRoleContents(
                    details,
                    EvidencePackageContentRoles.UnderlyingEvidence)
                .Any(content => content.Appendix is not null)
                ? AllReviewerAppendices
                : Array.Empty<string>();

        foreach (var appendix in appendices)
        {
            sections.Add(
                new PackageGuideSection(
                    AppendixHeading(appendix),
                    AppendixDescription(appendix)));
        }

        return sections;
    }

    private static bool HasPackageGuideSection(
        VeteransReviewerPackageDetails details,
        string title) =>
        GetPackageGuideSections(details)
            .Any(
                section =>
                    string.Equals(
                        section.Title,
                        title,
                        StringComparison.Ordinal));

    private static bool HasMedicalLiterature(
        VeteransReviewerPackageDetails details) =>
        GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence)
            .Any(
                content =>
                    string.Equals(
                        content.Appendix,
                        VeteransReviewerPackageAppendix.MedicalLiterature,
                        StringComparison.Ordinal));

    private static string BuildHistoricalMedicationOmissionMessage(
        VeteransReviewerPackageDetails details)
    {
        var availableSections =
            GetPackageGuideSections(details)
                .Select(section => section.Title)
                .ToHashSet(StringComparer.Ordinal);

        var references =
            new[]
            {
                CurrentMedicationSectionTitle,
                MedicationProgressionSectionTitle
            }
            .Where(availableSections.Contains)
            .ToArray();

        var message =
            "Historical medication table omitted from this reviewer copy because " +
            "it reflects a point-in-time source-record list rather than verified " +
            "current medication use. The original source remains preserved.";

        return references.Length switch
        {
            0 => message,
            1 => $"{message} See {references[0]}.",
            _ =>
                $"{message} See {string.Join(" and ", references)}."
        };
    }

    private static void AppendPackageGuideEntry(
        Body body,
        string title,
        string description)
    {
        // The guide is an orientation page, not a sequence of full subsections.
        // Keep each title and description in one compact paragraph so a normal
        // reviewer guide remains on one page.
        var properties =
            new ParagraphProperties(
                new KeepLines(),
                new SpacingBetweenLines
                {
                    Before = "0",
                    After = "40"
                });

        var titleProperties = ReviewerRunProperties("Heading3");
        var bodyProperties =
            new RunProperties(
                new RunFonts
                {
                    Ascii = VeteransReviewerFonts.Body,
                    HighAnsi = VeteransReviewerFonts.Body
                },
                new FontSize
                {
                    Val = "24"
                });

        body.Append(
            new Paragraph(
                properties,
                new Run(
                    titleProperties,
                    new Text(SanitizeXmlText(title + " — "))
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    }),
                new Run(
                    bodyProperties,
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
                "This package organizes evidence for independent medical review. " +
                "It does not make a medical, legal, or adjudicative conclusion."));
    }

    private static void AppendPapAdherenceSummary(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var clinicCompliance =
            GetPapClinicComplianceObservations(details);

        var sessionSummary =
            GetPapSessionAdherenceSummary(details);

        if (clinicCompliance.Count == 0 &&
            sessionSummary is null)
        {
            return;
        }

        body.Append(PageBreakParagraph());

        body.Append(
            StyledParagraph(
                "PAP Adherence / Compliance Summary",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The supplied records consistently document strong PAP adherence over " +
                "multiple years. This summary is presented before the appointment-by-" +
                "appointment chronology so residual AHI, mask leak, and treatment-change " +
                "findings can be reviewed in the context of documented PAP use."));

        if (clinicCompliance.Count > 0)
        {
            body.Append(
                ContentParagraph(
                    "Documented clinic compliance: " +
                    string.Join("; ", clinicCompliance) +
                    "."));
        }

        if (sessionSummary is not null)
        {
            body.Append(
                ContentParagraph(
                    $"{sessionSummary.SourceName} session summary " +
                    $"({sessionSummary.Coverage}): " +
                    $"{sessionSummary.SessionCount} sessions across " +
                    $"{sessionSummary.TreatmentDays} treatment days, " +
                    $"{sessionSummary.TotalTherapyHours} total therapy hours, " +
                    $"average {sessionSummary.AverageHoursPerTreatmentDay} hours per treatment day, " +
                    $"{sessionSummary.DaysAtLeastFourHours} days with at least 4 hours of use, " +
                    $"and {sessionSummary.DaysAtLeastSixHours} days with at least 6 hours of use. " +
                    $"Therapy metrics: weighted AHI {sessionSummary.WeightedAhi}, " +
                    $"median daily AHI {sessionSummary.MedianDailyAhi}, and " +
                    $"maximum daily AHI {sessionSummary.MaximumDailyAhi}."));
        }

        if (details.ClinicalProgressionEvents.Any(
                item =>
                    ContainsReviewerText(item.Summary, "residual AHI") &&
                    TryExtractPapCompliancePercent(item.Summary) is not null))
        {
            body.Append(
                ContentParagraph(
                    "The supplied chronology documents periods of elevated residual AHI " +
                    "during intervals that also show strong PAP adherence."));
        }
    }

    private static bool HasPapAdherenceSummary(
        VeteransReviewerPackageDetails details) =>
        GetPapClinicComplianceObservations(details).Count > 0 ||
        GetPapSessionAdherenceSummary(details) is not null;

    private static IReadOnlyList<string> GetPapClinicComplianceObservations(
        VeteransReviewerPackageDetails details)
    {
        var observations =
            new List<(DateOnly Date, string Percent)>();

        foreach (var item in details.ClinicalProgressionEvents)
        {
            var percent =
                TryExtractPapCompliancePercent(item.Summary);

            if (percent is null)
                continue;

            if (observations.Any(
                    existing =>
                        existing.Date == item.EventDate &&
                        string.Equals(
                            existing.Percent,
                            percent,
                            StringComparison.Ordinal)))
            {
                continue;
            }

            observations.Add((item.EventDate, percent));
        }

        return observations
            .OrderBy(item => item.Date)
            .Select(
                item =>
                    $"{item.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} — {item.Percent}%")
            .ToArray();
    }

    private static string? TryExtractPapCompliancePercent(
        string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        var forwardMatch =
            Regex.Match(
                summary,
                @"\b(?:PAP\s+)?compliance\s+(?:was\s+)?(?<percent>\d+(?:\.\d+)?)\s*%",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (forwardMatch.Success)
            return forwardMatch.Groups["percent"].Value;

        var reverseMatch =
            Regex.Match(
                summary,
                @"\b(?<percent>\d+(?:\.\d+)?)\s*%\s+(?:PAP\s+)?compliance\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return reverseMatch.Success
            ? reverseMatch.Groups["percent"].Value
            : null;
    }

    private static PapSessionAdherenceSummary? GetPapSessionAdherenceSummary(
        VeteransReviewerPackageDetails details)
    {
        foreach (var content in details.ArtifactContents)
        {
            if (!ContainsReviewerText(content.Text, "PAP Therapy Analysis"))
                continue;

            var coverage =
                GetReviewerLabeledValue(content.Text, "Coverage");
            var sessionCount =
                GetReviewerLabeledValue(content.Text, "Sessions");
            var treatmentDays =
                GetReviewerLabeledValue(content.Text, "Treatment days");
            var totalTherapyHours =
                GetReviewerLabeledValue(content.Text, "Total therapy hours");
            var averageHoursPerTreatmentDay =
                GetReviewerLabeledValue(
                    content.Text,
                    "Average hours per treatment day");
            var daysAtLeastFourHours =
                GetReviewerLabeledValue(content.Text, "Days >= 4 hours");
            var daysAtLeastSixHours =
                GetReviewerLabeledValue(content.Text, "Days >= 6 hours");
            var weightedAhi =
                GetReviewerLabeledValue(content.Text, "Weighted AHI");
            var medianDailyAhi =
                GetReviewerLabeledValue(content.Text, "Median daily AHI");
            var maximumDailyAhi =
                GetReviewerLabeledValue(content.Text, "Maximum daily AHI");

            if (string.IsNullOrWhiteSpace(coverage) ||
                string.IsNullOrWhiteSpace(sessionCount) ||
                string.IsNullOrWhiteSpace(treatmentDays) ||
                string.IsNullOrWhiteSpace(totalTherapyHours) ||
                string.IsNullOrWhiteSpace(averageHoursPerTreatmentDay) ||
                string.IsNullOrWhiteSpace(daysAtLeastFourHours) ||
                string.IsNullOrWhiteSpace(daysAtLeastSixHours) ||
                string.IsNullOrWhiteSpace(weightedAhi) ||
                string.IsNullOrWhiteSpace(medianDailyAhi) ||
                string.IsNullOrWhiteSpace(maximumDailyAhi))
            {
                continue;
            }

            var sourceName =
                ContainsReviewerText(
                    content.Text,
                    "retained SNORE session export")
                    ? "SNORE"
                    : ContainsReviewerText(
                        content.Text,
                        "retained OSCAR session export")
                        ? "OSCAR"
                        : "PAP";

            return new PapSessionAdherenceSummary(
                sourceName,
                coverage,
                sessionCount,
                treatmentDays,
                totalTherapyHours,
                averageHoursPerTreatmentDay,
                daysAtLeastFourHours,
                daysAtLeastSixHours,
                weightedAhi,
                medianDailyAhi,
                maximumDailyAhi);
        }

        return null;
    }

    private static string? GetReviewerLabeledValue(
        string text,
        string label)
    {
        var match =
            Regex.Match(
                text,
                $@"(?im)^\s*{Regex.Escape(label)}\s*:\s*(?<value>[^\r\n]+?)\s*$",
                RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["value"].Value.Trim()
            : null;
    }

    private static void AppendSleepStudyPapTitrationResults(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var items =
            GetPapTitrationSummaryItems(details);

        if (items.Count == 0)
            return;

        body.Append(
            StyledParagraph(
                "Sleep Study / PAP Titration Results",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The following PAP titration evidence includes primary study material " +
                "supplied in the medical-evidence appendix and titration findings " +
                "documented in provider notes. Historical studies are included to " +
                "preserve longitudinal PAP treatment context."));

        foreach (var item in items)
        {
            body.Append(
                StyledParagraph(
                    item.Heading,
                    "Heading2"));

            body.Append(
                ContentParagraph(item.Summary));

            body.Append(
                ContentParagraph(
                    $"Source: {item.SourceLocator}"));
        }

        var findings =
            GetPapTitrationFindings(details);

        if (findings.Count > 0 &&
            details.ClinicalProgressionEvents.Any(
                item =>
                    item.EventDate >= findings[0].EventDate &&
                    item.EventType == ClinicalProgressionEventTypes.TreatmentTransition &&
                    ContainsReviewerText(item.Summary, "ASV")))
        {
            body.Append(
                ContentParagraph(
                    "The subsequent treatment chronology—including documented pressure " +
                    "changes, ASV consultation or transition, and device setup when present " +
                    "in the supplied records—corroborates that these documented titration " +
                    "findings were incorporated into treatment decisions."));
        }
    }

    private static IReadOnlyList<PapTitrationSummaryItem>
        GetPapTitrationSummaryItems(
            VeteransReviewerPackageDetails details)
    {
        var items =
            new List<PapTitrationSummaryItem>();

        foreach (var content in
            details.ArtifactContents
                .Where(IsPrimaryPapTitrationEvidence))
        {
            var year =
                GetPapTitrationEvidenceYear(content);
            var sortDate =
                GetPapTitrationEvidenceSortDate(content);
            var heading =
                year is null
                    ? "Primary PAP Titration Study"
                    : $"{year.Value.ToString(CultureInfo.InvariantCulture)} — Primary PAP Titration Study";

            items.Add(
                new PapTitrationSummaryItem(
                    sortDate,
                    0,
                    heading,
                    "A primary PAP titration study is included in the medical-evidence appendix as historical treatment evidence. It is summarized here to preserve longitudinal PAP titration context alongside later documented findings.",
                    GetDisplayName(content)));
        }

        foreach (var finding in GetPapTitrationFindings(details))
        {
            items.Add(
                new PapTitrationSummaryItem(
                    finding.EventDate,
                    1,
                    $"{finding.EventDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} — PAP Titration Result",
                    finding.Summary,
                    finding.SourceLocator));
        }

        return items
            .OrderBy(item => item.SortDate is null ? 1 : 0)
            .ThenBy(item => item.SortDate)
            .ThenBy(item => item.SourceOrder)
            .ThenBy(item => item.SourceLocator, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<VeteransReviewerClinicalProgressionEvent>
        GetPapTitrationFindings(
            VeteransReviewerPackageDetails details) =>
        details.ClinicalProgressionEvents
            .Where(IsPapTitrationFinding)
            .OrderBy(item => item.EventDate)
            .ThenBy(item => item.SourceLocator, StringComparer.Ordinal)
            .ToArray();

    private static bool IsPrimaryPapTitrationEvidence(
        VeteransReviewerArtifactContent content)
    {
        if (!string.Equals(
                content.Appendix,
                VeteransReviewerPackageAppendix.MedicalEvidence,
                StringComparison.Ordinal))
        {
            return false;
        }

        var displayName =
            GetDisplayName(content);

        if (!ContainsReviewerText(displayName, "titration"))
            return false;

        return ContainsReviewerText(displayName, "PAP") ||
               ContainsReviewerText(displayName, "CPAP") ||
               ContainsReviewerText(displayName, "BiPAP") ||
               ContainsReviewerText(displayName, "ASV") ||
               ContainsReviewerText(displayName, "sleep");
    }

    private static DateOnly? GetPapTitrationEvidenceSortDate(
        VeteransReviewerArtifactContent content)
    {
        var evidenceDate =
            GetEvidenceDate(content);

        if (DateOnly.TryParseExact(
                evidenceDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            return parsedDate;
        }

        var filenameDate =
            Regex.Match(
                content.Artifact.Name,
                @"(?<!\d)(?<year>(?:19|20)\d{2})[-_.](?<month>0[1-9]|1[0-2])[-_.](?<day>0[1-9]|[12]\d|3[01])(?!\d)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        if (filenameDate.Success &&
            DateOnly.TryParseExact(
                $"{filenameDate.Groups["year"].Value}-{filenameDate.Groups["month"].Value}-{filenameDate.Groups["day"].Value}",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate))
        {
            return parsedDate;
        }

        var year =
            GetPapTitrationEvidenceYear(content);

        return year is null
            ? null
            : new DateOnly(year.Value, 1, 1);
    }

    private static int? GetPapTitrationEvidenceYear(
        VeteransReviewerArtifactContent content)
    {
        var evidenceDate =
            GetEvidenceDate(content);

        if (DateOnly.TryParseExact(
                evidenceDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            return parsedDate.Year;
        }

        var match =
            Regex.Match(
                content.Artifact.Name,
                @"(?<!\d)(?<year>(?:19|20)\d{2})(?!\d)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        return match.Success &&
               int.TryParse(
                   match.Groups["year"].Value,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var year)
            ? year
            : null;
    }

    private sealed record PapTitrationSummaryItem(
        DateOnly? SortDate,
        int SourceOrder,
        string Heading,
        string Summary,
        string SourceLocator);

    private static bool IsPapTitrationFinding(
        VeteransReviewerClinicalProgressionEvent item) =>
        string.Equals(
            item.EventType,
            ClinicalProgressionEventTypes.DiagnosticFinding,
            StringComparison.Ordinal) &&
        ContainsReviewerText(item.Summary, "PAP titration");

    private sealed record PapSessionAdherenceSummary(
        string SourceName,
        string Coverage,
        string SessionCount,
        string TreatmentDays,
        string TotalTherapyHours,
        string AverageHoursPerTreatmentDay,
        string DaysAtLeastFourHours,
        string DaysAtLeastSixHours,
        string WeightedAhi,
        string MedianDailyAhi,
        string MaximumDailyAhi);

    private static void AppendClinicalProgression(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var progression =
            details.ClinicalProgressionEvents
                .Where(item => !IsPapTitrationFinding(item))
                .OrderBy(item => item.EventDate)
                .ThenBy(item => item.SourceLocator, StringComparer.Ordinal)
                .ThenBy(item => item.EventType, StringComparer.Ordinal)
                .ThenBy(item => item.Summary, StringComparer.Ordinal)
                .ToArray();

        if (progression.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                "Clinical Progression",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "This source-grounded progression highlights clinically meaningful " +
                "treatment use, problems, adjustments, diagnostic findings, transitions, " +
                "and responses documented in the supplied records. It does not infer " +
                "causation or resolve conflicts that are not resolved in the source record. " +
                "These factual observations are not independent medical nexus opinions."));

        foreach (var item in progression)
        {
            body.Append(
                StyledParagraph(
                    $"{item.EventDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)} — " +
                    ClinicalProgressionEventTypeDisplayName(item.EventType),
                    "Heading2"));

            body.Append(
                ContentParagraph(item.Summary));

            body.Append(
                ContentParagraph(
                    $"Source: {item.SourceLocator}"));
        }
    }

    private static string ClinicalProgressionEventTypeDisplayName(
        string eventType) =>
        eventType switch
        {
            ClinicalProgressionEventTypes.TreatmentUse =>
                "Treatment Use",
            ClinicalProgressionEventTypes.TreatmentProblem =>
                "Treatment Problem",
            ClinicalProgressionEventTypes.TreatmentAdjustment =>
                "Treatment Adjustment",
            ClinicalProgressionEventTypes.DiagnosticFinding =>
                "Diagnostic Finding",
            ClinicalProgressionEventTypes.TreatmentTransition =>
                "Treatment Transition",
            ClinicalProgressionEventTypes.TreatmentResponse =>
                "Treatment Response",
            _ => throw new InvalidOperationException(
                "Unsupported reviewer clinical progression event type.")
        };

    private static void AppendMedicationProgressions(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        var packageBasisId =
            details.PackageDetails.Package.ServiceConnectionBasisId;

        var progressions =
            details.MedicationProgressions
                .OrderBy(item =>
                    item.ServiceConnectionBasisId == packageBasisId ? 0 : 1)
                .ThenBy(
                    item => item.ServiceConnectionBasisReviewerLabel ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    item => item.MedicationName,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (progressions.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                MedicationProgressionSectionTitle,
                "Heading1"));

        body.Append(
            MedicationParagraph(
                "This section summarizes VA prescription history relevant to the medical opinion, " +
                "grouped by service-connected condition when applicable. Dated changes in dose, " +
                "directions and recorded status are retained. Each status is the status recorded " +
                "in the cited report; the prescription date is not a discontinuation date or " +
                "confirmation of current use. Non-VA and unverified-source entries are excluded " +
                "from this assembled list. Attributed indication reconciliations are shown " +
                "separately from the unchanged VA ledger wording."));

        var showBasisGroups =
            progressions.Any(item =>
                item.ServiceConnectionBasisId is not null ||
                !string.IsNullOrWhiteSpace(
                    item.ServiceConnectionBasisReviewerLabel));

        string? currentBasisKey = null;

        foreach (var progression in progressions)
        {
            if (showBasisGroups)
            {
                var basisKey =
                    progression.ServiceConnectionBasisId?.Value ??
                    progression.ServiceConnectionBasisReviewerLabel?.Trim() ??
                    string.Empty;

                if (!string.Equals(
                        currentBasisKey,
                        basisKey,
                        StringComparison.Ordinal))
                {
                    body.Append(
                        StyledParagraph(
                            MedicationBasisDisplayName(
                                progression.ServiceConnectionBasisReviewerLabel),
                            "Heading2"));

                    currentBasisKey = basisKey;
                }
            }

            body.Append(
                StyledParagraph(
                    progression.MedicationName,
                    "Heading3",
                    bold: true));

            if (progression.IndicationReconciliation is { } reconciliation)
                body.Append(MedicationParagraph(
                    $"Indication reconciliation — {reconciliation.Source}, " +
                    reconciliation.ReconciliationDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) +
                    $": {reconciliation.Indication}. VA ledger wording is retained below.",
                    keepWithNext: true));

            foreach (var entry in progression.Entries)
            {
                var recordParagraphs = new List<Paragraph>();
                var eventDate =
                    entry.PrescribedDate ??
                    entry.LastFilledDate;

                var summary = new List<string>();

                if (eventDate is not null)
                    summary.Add((entry.PrescribedDate is not null ? "Prescribed " : "Last filled ") +
                        eventDate.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture));

                summary.Add("VA ledger status: " + MedicationLedgerStatusDisplayName(entry.Status));

                if (!string.IsNullOrWhiteSpace(entry.Strength))
                    summary.Add(entry.Strength.Trim());

                recordParagraphs.Add(
                    MedicationParagraph(
                        string.Join(" — ", summary)));

                if (!string.IsNullOrWhiteSpace(entry.Directions))
                {
                    recordParagraphs.Add(
                        MedicationParagraph(
                            $"VA ledger directions: {entry.Directions.Trim()}" +
                            (entry.RefillsLeft is not null && !entry.Directions.Contains("Refills left:", StringComparison.OrdinalIgnoreCase)
                                ? $" Refills left: {entry.RefillsLeft}" : string.Empty)));
                }

                if (!string.IsNullOrWhiteSpace(entry.Indication) &&
                    !entry.Indication.Equals("None recorded", StringComparison.OrdinalIgnoreCase))
                    recordParagraphs.Add(MedicationParagraph($"VA ledger indication: {entry.Indication.Trim()}"));
                if (entry.RefillsLeft is not null && string.IsNullOrWhiteSpace(entry.Directions))
                    recordParagraphs.Add(MedicationParagraph($"Refills left: {entry.RefillsLeft}"));
                if (progression.EntrySources.TryGetValue(entry.Id, out var entrySource))
                    recordParagraphs.Add(MedicationParagraph("Source: " + entrySource));

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
                        recordParagraphs.Add(
                            MedicationParagraph(
                                $"Documented clinical context: {context.Summary}"));
                        recordParagraphs.Add(
                            MedicationParagraph(
                                $"Source: {context.SourceLocator}"));
                    }
                }

                // A dated record is one reading unit, including its attribution and context.
                // Leave the final paragraph unlinked so separate records can paginate freely.
                foreach (var paragraph in recordParagraphs.Take(recordParagraphs.Count - 1))
                    paragraph.ParagraphProperties!.AddChild(new KeepNext(), true);
                body.Append(recordParagraphs);
            }
        }
    }

    private static string MedicationBasisDisplayName(
        string? reviewerLabel)
    {
        if (string.IsNullOrWhiteSpace(reviewerLabel))
            return "Relevant medications";

        const string secondaryMedicationPrefix =
            "Secondary to medications used for service-connected ";

        var label = reviewerLabel.Trim().TrimEnd('.');

        if (label.StartsWith(
                secondaryMedicationPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            var condition =
                label[secondaryMedicationPrefix.Length..].Trim();

            if (condition.Length > 0)
                return $"Medications for service-connected {condition}";
        }

        return label;
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
                CurrentMedicationSectionTitle,
                "Heading1"));

        body.Append(
            MedicationParagraph(
                "Only medications explicitly confirmed as currently used through " +
                "medication reconciliation are listed here. VA prescription " +
                "status alone is not treated as verified current use. The underlying " +
                "VA medication ledger remains preserved unchanged."));

        foreach (var medication in medications)
        {
            body.Append(
                StyledParagraph(
                    medication.MedicationName,
                    "Heading3"));

            if (!string.IsNullOrWhiteSpace(medication.Strength))
                body.Append(MedicationParagraph(
                    $"Strength: {medication.Strength.Trim()}"));

            if (!string.IsNullOrWhiteSpace(medication.Directions))
                body.Append(MedicationParagraph(
                    $"Directions: {medication.Directions.Trim()}"));

            if (!string.IsNullOrWhiteSpace(medication.Indication) &&
                !string.Equals(
                    medication.Indication.Trim(),
                    "None recorded",
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(MedicationParagraph(
                    $"Documented indication: {medication.Indication.Trim()}"));
            }

            body.Append(
                MedicationParagraph(
                    $"VA prescription status: {MedicationLedgerStatusDisplayName(medication.Status)}"));

            body.Append(
                MedicationParagraph(
                    "Current use: Confirmed during medication reconciliation"));
        }
    }

    private static void AppendPackagePrescriptionList(Body body, VeteransReviewerArtifactContent presentation)
    {
        var lines = presentation.Text.Split('\n');
        body.Append(StyledParagraph(lines[0], "Heading1"));
        var inMedicationGroup = false;
        var entry = new List<Paragraph>();
        foreach (var line in lines.Skip(1))
        {
            if (line is "Claim-relevant psychiatric prescriptions" or "Other VA prescriptions")
            {
                body.Append(StyledParagraph(line, "Heading2"));
                inMedicationGroup = true;
            }
            else if (!inMedicationGroup)
                body.Append(MedicationParagraph(line, keepWithNext: true));
            else
            {
                var last = line.StartsWith("VA status:", StringComparison.Ordinal);
                var paragraph = MedicationParagraph(line, keepWithNext: !last);
                entry.Add(paragraph);
                if (!last) continue;
                AppendPrescriptionEntry(body, entry);
                entry.Clear();
            }
        }
        if (entry.Count > 0)
            AppendPrescriptionEntry(body, entry);
    }

    private static void AppendPrescriptionEntry(Body body, IReadOnlyList<Paragraph> paragraphs)
    {
        // Keep a fitting entry in one borderless row without changing its text
        // formatting. Separate tables let an oversized entry flow across pages
        // without LibreOffice clipping its end against the following row.
        var cell = new TableCell(new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));
        cell.Append(paragraphs);
        body.Append(new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Nil },
                    new LeftBorder { Val = BorderValues.Nil },
                    new BottomBorder { Val = BorderValues.Nil },
                    new RightBorder { Val = BorderValues.Nil },
                    new InsideHorizontalBorder { Val = BorderValues.Nil },
                    new InsideVerticalBorder { Val = BorderValues.Nil }),
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableCellMarginDefault(
                    new TopMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 0, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "0", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 0, Type = TableWidthValues.Dxa })),
            new TableGrid(new GridColumn { Width = "9360" }),
            new TableRow(new TableRowProperties(new CantSplit()), cell)));
        // Prevent adjacent tables from merging; use only a one-twip separator.
        body.Append(new Paragraph(new ParagraphProperties(new SpacingBetweenLines
        {
            Before = "0", After = "0", Line = "1", LineRule = LineSpacingRuleValues.Exact
        })));
    }

    private static Paragraph MedicationParagraph(string text, bool keepWithNext = false) => ContentParagraph(
        Regex.Replace(text, @"\bRefills:\s*\d+\.(?:\s+Refills left:\s*\d+)?|\bRefills(?: left)?:\s*\d+\.?", match =>
            Regex.Replace(match.Value, @"\s+", "\u00a0"), RegexOptions.IgnoreCase), keepWithNext: keepWithNext);

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

        body.Append(PageBreakParagraph());

        body.Append(
            StyledParagraph(
                "Key Evidence and Chronology",
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The chronology below uses evidence dates documented in the " +
                "supplied source records. Medical sources without a documented evidence " +
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
            var pages =
                GetSourcePageReference(content);

            var entry = StyledParagraph(
                $"{GetEvidenceDate(content)} — {GetDisplayName(content)}",
                "Heading2");
            // An entry without a reference is a complete list item. Chaining
            // consecutive headings pushes the whole chronology off its intro.
            entry.ParagraphProperties!.GetFirstChild<KeepNext>()!.Val =
                !string.IsNullOrWhiteSpace(pages);
            entry.ParagraphProperties.AddChild(new KeepLines(), true);
            body.Append(entry);

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

        body.Append(PageBreakParagraph());

        body.Append(
            StyledParagraph(
                MedicalLiteratureSectionTitle,
                "Heading1"));

        body.Append(
            ContentParagraph(
                "The following literature is included for the reviewing physician. " +
                "Complete source material is preserved in Appendix F."));

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

                var requirementLabel =
                    RequirementLabel(
                        reviewed.Association.RequirementId.Value);

                body.Append(
                    ContentParagraph(
                        requirementLabel is null
                            ? $"Role: {GuidanceRoleDisplayName(reviewed.Association.GuidanceRole)}"
                            : $"Role: {requirementLabel} — {GuidanceRoleDisplayName(reviewed.Association.GuidanceRole)}"));

                body.Append(
                    ContentParagraph(
                        $"Relevance: {reviewed.Association.Description}"));
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

    private static void AppendApplicableRegulations(
        Body body,
        IReadOnlyList<VeteransReviewerApplicableRegulation> regulations)
    {
        body.Append(
            StyledParagraph(
                "Applicable VA Regulation",
                "Heading1"));

        foreach (var regulation in regulations)
        {
            if (string.IsNullOrWhiteSpace(regulation.Citation) ||
                string.IsNullOrWhiteSpace(regulation.Text))
            {
                throw new InvalidOperationException(
                    "Applicable reviewer regulation is incomplete.");
            }

            body.Append(
                ReviewerSubsectionHeading(regulation.Citation.Trim()));

            var paragraphs =
                regulation.Text
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split(
                        "\n\n",
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

            foreach (var paragraph in paragraphs)
            {
                body.Append(
                    ContentParagraph(paragraph));
            }

            body.Append(
                ContentParagraph(
                    "Source: Electronic Code of Federal Regulations (eCFR), " +
                    $"current through {regulation.UpToDateAsOf:MMMM d, yyyy}; " +
                    $"retrieved {regulation.RetrievedUtc:MMMM d, yyyy} UTC."));
        }
    }

    private static void AppendEvidenceAppendices(
        MainDocumentPart mainPart,
        Body body,
        VeteransReviewerPackageDetails details,
        VeteransReviewerEvidenceSections sections,
        DateOnly sourceReviewDate)
    {
        var prescriptionList = GetRoleContents(details, EvidencePackageContentRoles.GeneratedOrganizationalMaterial)
            .SingleOrDefault(c => c.Artifact.ArtifactType == VeteransReviewerPackagePrescriptionPresentation.ArtifactType);
        if (prescriptionList is not null)
            VeteransReviewerPackagePrescriptionPresentation.ValidateFrozen(details, prescriptionList);
        var prescriptionListRendered = false;
        var contents =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence);

        if (contents.Count == 0)
            return;

        var appendixGroups =
            contents
                .Where(content => content.Appendix is not null)
                .GroupBy(content => content.Appendix!)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);

        if (prescriptionList is not null && !contents.Any(c => BuildHistoricalMedicationTitle(c) is not null))
        {
            AppendGeneratedSection(body, sections, prescriptionList.Artifact.Name,
                section => AppendPackagePrescriptionList(section, prescriptionList));
            prescriptionListRendered = true;
        }
        foreach (var appendix in AllReviewerAppendices)
        {
            sections.Start(body, null);

            body.Append(
                StyledParagraph(
                    AppendixHeading(appendix),
                    "Heading1"));

            body.Append(
                ContentParagraph(
                    AppendixDescription(appendix)));

            if (!appendixGroups.TryGetValue(
                    appendix,
                    out var groupContents))
            {
                body.Append(
                    ContentParagraph(
                        EmptyAppendixMessage(appendix)));
                continue;
            }

            foreach (var content in
                groupContents
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
                if (!prescriptionListRendered && prescriptionList is not null &&
                    BuildHistoricalMedicationTitle(content) is not null)
                {
                    AppendGeneratedSection(body, sections, prescriptionList.Artifact.Name,
                        section => AppendPackagePrescriptionList(section, prescriptionList));
                    prescriptionListRendered = true;
                }
                sections.Start(body, SanitizeXmlText(GetDisplayName(content)));

                AppendSourceContent(
                    mainPart,
                    body,
                    details,
                    content,
                    sections,
                    sourceReviewDate,
                    prescriptionListRendered);
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

        sections.Start(body, null);

        body.Append(
            StyledParagraph(
                "Additional Evidence",
                "Heading1"));

        foreach (var content in additionalEvidence)
        {
            sections.Start(body, SanitizeXmlText(GetDisplayName(content)));

            AppendSourceContent(
                mainPart,
                body,
                details,
                content,
                sections,
                sourceReviewDate);
        }
    }

    private static void AppendSourceContent(
        MainDocumentPart mainPart,
        Body body,
        VeteransReviewerPackageDetails details,
        VeteransReviewerArtifactContent content,
        VeteransReviewerEvidenceSections sections,
        DateOnly sourceReviewDate,
        bool hasPackagePrescriptionList = false)
    {
        var storedSelection = details.PackageDetails.Artifacts
            .Single(item => item.ArtifactId == content.Artifact.Id).ReviewerPageSelection;
        var pages = storedSelection is null ? content.PrintablePages :
            VeteransReviewerPageSelector.Select(content.PrintablePages, storedSelection);

        var displayName =
            GetDisplayName(content);

        var sourceReference =
            BuildSourceReference(content);

        var sourceName = GetSourceName(content);

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

        ValidateReviewerSourceCorrections(
            content,
            clarifications);

        var historicalMedicationTitle = BuildHistoricalMedicationTitle(content);
        var historicalMedicationOmissionMessage = historicalMedicationTitle is null
            ? null : BuildHistoricalMedicationOmissionMessage(details);

        var presentation = VeteransReviewerEvidencePresentation.Create(
            content,
            pages
                .Where(page => string.Equals(page.ContentType, "text/plain",
                    StringComparison.OrdinalIgnoreCase))
                .Select(page => DecodePrintableText(page.Content)),
            details.VeteranDisplayName);

        AppendSourcePreamble(body, displayName, sourceReference, sourceName, clarifications,
            includeDisplayHeading: true);

        var sourceChecks = VeteransReviewerSourceReasonablenessReview.Review(content, pages, sourceReviewDate)
            .Where(finding => !clarifications.Any(c =>
                c.OriginalText.Contains(finding.Value, StringComparison.OrdinalIgnoreCase) &&
                (c.Clarification.Contains(finding.Value, StringComparison.OrdinalIgnoreCase) ||
                 c.ReviewerMatchText?.Contains(finding.Value, StringComparison.OrdinalIgnoreCase) == true)))
            .GroupBy(finding => (finding.Rule, finding.Value, finding.Message))
            .ToArray();
        if (sourceChecks.Length > 0)
        {
            body.Append(StyledParagraph("Source Data Warning — Review Required", "Heading3"));
            body.Append(ContentParagraph(
                "Automated reasonableness checks identified the following source values. " +
                "Verify them before relying on them. The original evidence is preserved unchanged."));
            foreach (var group in sourceChecks)
            {
                var finding = group.First();
                var locations = group.Where(f => f.SourcePage.HasValue).Select(f => f.SourcePage!.Value)
                    .Distinct().Order().ToArray();
                var location = locations.Length == 0 ? "Source record" : "Source page(s) " + string.Join(", ", locations);
                body.Append(ContentParagraph($"{location}: {finding.SourceText}", keepWithNext: true));
                body.Append(ContentParagraph(finding.Message));
            }
        }

        if (content.IsExtractedTextFallback)
            body.Append(ContentParagraph(
                "Extracted-text fallback: authoritative native source pages are not available for this excerpt. " +
                "The text may not preserve the original form, table, or column layout."));

        var medicalLiterature = string.Equals(content.Appendix,
            VeteransReviewerPackageAppendix.MedicalLiterature, StringComparison.Ordinal);
        // Reviewed summaries/relevance remain in the package's literature section.
        // A publication's extracted reviewer text must never displace source pages.
        if (pages.Count > 0)
        {
            if (medicalLiterature && pages.All(page =>
                    string.Equals(page.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)))
            {
                AppendLiteratureSourcePages(mainPart, body, pages, sections,
                    storedSelection is not null || content.PrintableSourceArtifactId is not null);
                return;
            }
            AppendPrintablePages(mainPart, body, pages,
                clarifications, storedSelection is not null || content.PrintableSourceArtifactId is not null, medicalLiterature,
                presentation, historicalMedicationTitle, historicalMedicationOmissionMessage,
                allowLargerSinglePageImage:
                    content.Appendix == VeteransReviewerPackageAppendix.LayEvidence,
                medicalEvidence: content.Appendix == VeteransReviewerPackageAppendix.MedicalEvidence,
                artifactTitle: GetDisplayName(content),
                hasPackagePrescriptionList: hasPackagePrescriptionList);
        }
        else if (medicalLiterature)
        {
            AppendMedicalLiteratureText(body, ApplyReviewerSourceCorrections(
                content.MedicalLiteratureReviewerText ?? content.Text, clarifications));
        }
        else if (!string.IsNullOrWhiteSpace(content.Text))
        {
            AppendReviewerText(body, ApplyReviewerSourceCorrections(content.Text, clarifications),
                presentation, historicalMedicationTitle, historicalMedicationOmissionMessage);
        }

        if (presentation.Corrections.Count > 0)
            body.Append(ContentParagraph(
                "Reviewer transcription corrections (original source unchanged): " +
                string.Join("; ", presentation.Corrections.Select(c =>
                    $"“{c.Original}” → “{c.Replacement}”").Distinct(StringComparer.Ordinal)) + "."));
    }

    private static void AppendSourcePreamble(
        Body body,
        string displayName,
        string? sourceReference,
        string? sourceName,
        IReadOnlyList<VeteransReviewerSourceClarification> clarifications,
        bool includeDisplayHeading)
    {
        if (includeDisplayHeading)
        {
            body.Append(
                StyledParagraph(
                    displayName,
                    "Heading2"));
        }

        if (!string.IsNullOrWhiteSpace(sourceReference))
        {
            body.Append(
                ContentParagraph(
                    sourceReference,
                    keepWithNext: true));
        }

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

        AppendSourceClarifications(
            body,
            clarifications);
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

            if (!string.IsNullOrWhiteSpace(
                    clarification.ReviewerReplacementText))
            {
                body.Append(
                    ContentParagraph(
                        $"Veteran-reported correction: " +
                        $"{clarification.ReviewerReplacementText.Trim()}",
                        keepWithNext: true));

                body.Append(
                    ContentParagraph(
                        "The original source record is preserved unchanged; " +
                        "this reviewer copy applies the persisted correction."));
                continue;
            }

            body.Append(
                ContentParagraph(
                    $"Source text: {clarification.OriginalText}",
                    keepWithNext: true));

            body.Append(
                ContentParagraph(
                    clarification.Clarification));
        }
    }

    private static void ValidateReviewerSourceCorrections(
        VeteransReviewerArtifactContent content,
        IReadOnlyList<VeteransReviewerSourceClarification> clarifications)
    {
        var matchTexts =
            clarifications
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.ReviewerMatchText))
                .Select(item => item.ReviewerMatchText!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        if (matchTexts.Length == 0)
            return;

        var reviewerText = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(content.Text))
            reviewerText.Append(content.Text);

        foreach (var page in content.PrintablePages)
        {
            if (!string.Equals(
                    page.ContentType,
                    "text/plain",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            reviewerText.AppendLine();
            reviewerText.Append(DecodePrintableText(page.Content));
        }

        var text = reviewerText.ToString();

        foreach (var matchText in matchTexts)
        {
            if (!text.Contains(matchText, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Persisted reviewer source correction does not match " +
                    "the reviewer text for its associated evidence item.");
            }
        }
    }

    private static string ApplyReviewerSourceCorrections(
        string text,
        IReadOnlyList<VeteransReviewerSourceClarification> clarifications)
    {
        var corrected = text;

        foreach (var clarification in clarifications)
        {
            if (string.IsNullOrWhiteSpace(clarification.ReviewerMatchText) ||
                string.IsNullOrWhiteSpace(
                    clarification.ReviewerReplacementText))
            {
                continue;
            }

            corrected = corrected.Replace(
                clarification.ReviewerMatchText.Trim(),
                clarification.ReviewerReplacementText.Trim(),
                StringComparison.Ordinal);
        }

        return corrected;
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
            VeteransReviewerPackageAppendix.MedicalOpinionEvidence =>
                "Medical Opinion Evidence",
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
        if (string.Equals(
                content.Artifact.ArtifactType,
                "veterans-clinical-note",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

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

    private static readonly string[] AllReviewerAppendices =
    [
        VeteransReviewerPackageAppendix.MedicalEvidence,
        VeteransReviewerPackageAppendix.MedicalOpinionEvidence,
        VeteransReviewerPackageAppendix.ServiceRecords,
        VeteransReviewerPackageAppendix.LayEvidence,
        VeteransReviewerPackageAppendix.AdjudicativeRecords,
        VeteransReviewerPackageAppendix.MedicalLiterature
    ];

    private static string EmptyAppendixMessage(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "No medical evidence is included in this package.",
            VeteransReviewerPackageAppendix.MedicalOpinionEvidence =>
                "No medical opinion evidence is included in this package.",
            VeteransReviewerPackageAppendix.ServiceRecords =>
                "No service records are included in this package.",
            VeteransReviewerPackageAppendix.LayEvidence =>
                "No lay evidence is included in this package.",
            VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                "No adjudicative records are included in this package.",
            VeteransReviewerPackageAppendix.MedicalLiterature =>
                "No medical/scientific literature is included in this package.",
            _ =>
                "No evidence is included in this appendix."
        };

    private static string? RequirementLabel(string requirementId)
    {
        if (requirementId.Contains(
                "causation",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Causation";
        }

        if (requirementId.Contains(
                "aggravation",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Aggravation";
        }

        return null;
    }

    private static int AppendixOrder(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence => 0,
            VeteransReviewerPackageAppendix.MedicalOpinionEvidence => 1,
            VeteransReviewerPackageAppendix.ServiceRecords => 2,
            VeteransReviewerPackageAppendix.LayEvidence => 3,
            VeteransReviewerPackageAppendix.AdjudicativeRecords => 4,
            VeteransReviewerPackageAppendix.MedicalLiterature => 5,
            _ => int.MaxValue
        };

    private static string AppendixHeading(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "Appendix A — Medical Evidence",
            VeteransReviewerPackageAppendix.MedicalOpinionEvidence =>
                "Appendix B — Medical Opinion Evidence",
            VeteransReviewerPackageAppendix.ServiceRecords =>
                "Appendix C — Service Records",
            VeteransReviewerPackageAppendix.LayEvidence =>
                "Appendix D — Lay Evidence",
            VeteransReviewerPackageAppendix.AdjudicativeRecords =>
                "Appendix E — Adjudicative Records",
            VeteransReviewerPackageAppendix.MedicalLiterature =>
                "Appendix F — Medical / Scientific Literature",
            _ => appendix
        };

    private static string AppendixDescription(string appendix) =>
        appendix switch
        {
            VeteransReviewerPackageAppendix.MedicalEvidence =>
                "Contains clinical notes, diagnostic reports, treatment records, and related medical evidence.",
            VeteransReviewerPackageAppendix.MedicalOpinionEvidence =>
                "Contains medical opinion and nexus evidence supplied for review.",
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
                new TableGrid(new GridColumn { Width = "7488" }, new GridColumn { Width = "1872" }),
                new TableRow(
                    FooterCell(
                        4000,
                        JustificationValues.Center,
                        noWrap: false,
                        FooterRun(
                            "CONFIDENTIAL — VETERAN MEDICAL INFORMATION",
                            bold: true),
                        FooterRun(
                            "  |  Veterans Evidence Package for Medical Review")),
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
                    new SpacingBetweenLines
                    {
                        Before = "120"
                    },
                    new Justification
                    {
                        Val = justification
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
                    Ascii = VeteransReviewerFonts.Body,
                    HighAnsi = VeteransReviewerFonts.Body
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
                new SpacingBetweenLines { Before = "240", After = "240" },
                new Justification { Val = JustificationValues.Center });

        return new Paragraph(
            properties,
            new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = VeteransReviewerFonts.Body,
                        HighAnsi = VeteransReviewerFonts.Body
                    },
                    new Bold(),
                    new FontSize
                    {
                        Val = "36"
                    }),
                new Text(
                    "CONFIDENTIAL — VETERAN MEDICAL INFORMATION")));
    }

    private static Styles ReviewerStyles() =>
        new(
            ReviewerParagraphStyle(
                "Normal",
                "Normal",
                isDefault: true),
            ReviewerParagraphStyle(
                "Title",
                "Title"),
            ReviewerParagraphStyle(
                "Subtitle",
                "Subtitle"),
            ReviewerParagraphStyle(
                "Heading1",
                "heading 1"),
            ReviewerParagraphStyle(
                "Heading2",
                "heading 2"),
            ReviewerParagraphStyle(
                "Heading3",
                "heading 3"));

    private static Style ReviewerParagraphStyle(
        string styleId,
        string name,
        bool isDefault = false)
    {
        var style =
            new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = styleId
            };

        if (isDefault)
            style.Default = true;

        style.Append(
            new StyleName
            {
                Val = name
            });

        if (!isDefault)
        {
            style.Append(
                new BasedOn
                {
                    Val = "Normal"
                });
        }

        return style;
    }

    private static Paragraph StyledParagraph(
        string text,
        string styleId,
        bool bold = false)
    {
        var properties =
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = styleId
                });

        var isTitle =
            string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal);
        var isHeading1 =
            string.Equals(
                styleId,
                "Heading1",
                StringComparison.Ordinal);
        var isHeading2 =
            string.Equals(
                styleId,
                "Heading2",
                StringComparison.Ordinal);
        var isSubtitle =
            string.Equals(
                styleId,
                "Subtitle",
                StringComparison.Ordinal);

        var isHeading3 = styleId == "Heading3";

        if (isHeading1 || isHeading2 || isHeading3)
            properties.AddChild(new KeepNext(), true);

        if (isTitle)
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "720",
                    After = "360"
                });

            properties.Append(
                new Justification
                {
                    Val = JustificationValues.Center
                });
        }
        else if (isHeading1)
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "180",
                    After = "120"
                });
        }
        else if (isHeading2 || isHeading3)
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "60"
                });
        }
        else if (isSubtitle)
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    After = "60"
                });

            properties.Append(
                new Justification
                {
                    Val = JustificationValues.Center
                });
        }

        var runProperties = ReviewerRunProperties(styleId);

        if (bold && runProperties.GetFirstChild<Bold>() is null)
            runProperties.AddChild(new Bold(), true);

        return new Paragraph(
            properties,
            new Run(
                runProperties,
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
                    Ascii = VeteransReviewerFonts.Body,
                    HighAnsi = VeteransReviewerFonts.Body
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
            properties.Append(new Color { Val = VeteransReviewerEvidenceSections.EvidenceTitleColor });
            properties.Append(new FontSize { Val = "26" });
        }
        else if (styleId == "Heading3")
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = VeteransReviewerEvidenceSections.EvidenceTitleColor });
            properties.Append(new FontSize { Val = "24" });
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
            new ParagraphProperties(
                new PageBreakBefore()));

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

        properties.AddChild(new KeepLines(), true);

        if (keepWithNext)
            properties.AddChild(new KeepNext(), true);

        return new Paragraph(
            properties,
            new Run(
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = VeteransReviewerFonts.Body,
                        HighAnsi = VeteransReviewerFonts.Body
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
        string text,
        VeteransReviewerEvidencePresentation presentation,
        string? historicalMedicationTitle = null,
        string? historicalMedicationOmissionMessage = null)
    {
        var historicalMedicationTitleRendered = false;
        var suppressHistoricalMedicationSection = false;
        var protectedClinicalSection = false;
        var sourceText = presentation.Normalize(text);
        var blocks = VeteransReviewerTextLayout.Project(sourceText);
        // A standalone scalar can occur inside wrapped ordinary prose. Its
        // typography must not disable the existing non-source-specific reflow.
        IReadOnlyList<VeteransReviewerTextBlock> layoutBlocks = presentation.IsBlueButton ||
            blocks.Any(block => block.Shape == VeteransReviewerTextShape.Preformatted)
            ? blocks
            : NormalizeReviewerText(sourceText).Select((line, index) =>
                new VeteransReviewerTextBlock(line, VeteransReviewerTextLayout.Classify(line), index + 1)).ToArray();

        // Terminal line delimiters are not an empty continuation page. Keep
        // every interior blank row, but do not render trailing blank paragraphs
        // after this source page's last content. Stored source text is unchanged.
        var contentEnd = layoutBlocks.Count;
        while (contentEnd > 0 && layoutBlocks[contentEnd - 1].Shape == VeteransReviewerTextShape.Blank)
            contentEnd--;

        for (var lineIndex = 0;
             lineIndex < contentEnd;
             lineIndex++)
        {
            var block = layoutBlocks[lineIndex];
            var line = block.Text;
            var preformatted = block.Shape is VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow;
            if (preformatted)
                line = ExpandReviewerTabs(line);

            if (block.Shape == VeteransReviewerTextShape.Blank)
            {
                if (!suppressHistoricalMedicationSection)
                {
                    // A source blank line is one visual row, not a full prose
                    // paragraph with additional after-spacing. Keep each blank
                    // boundary and any source whitespace without amplifying it.
                    var besidePreformatted =
                        lineIndex > 0 && layoutBlocks[lineIndex - 1].Shape is VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow ||
                        lineIndex + 1 < layoutBlocks.Count && layoutBlocks[lineIndex + 1].Shape is VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow;
                    body.Append(new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                        new Run(new RunProperties(new FontSize { Val = besidePreformatted ? "18" : "24" }),
                            new Text(SanitizeXmlText(line)) { Space = SpaceProcessingModeValues.Preserve })));
                }
                continue;
            }

            if (!historicalMedicationTitleRendered &&
                !string.IsNullOrWhiteSpace(historicalMedicationTitle) &&
                IsHistoricalMedicationSectionHeading(line))
            {
                body.Append(
                    StyledParagraph(
                        historicalMedicationTitle,
                        "Heading3"));

                body.Append(
                    ContentParagraph(
                        historicalMedicationOmissionMessage ??
                        "Historical medication table omitted from this reviewer copy because " +
                        "it reflects a point-in-time source-record list rather than verified " +
                        "current medication use. The original source remains preserved."));

                historicalMedicationTitleRendered = true;
                suppressHistoricalMedicationSection = true;
                continue;
            }

            if (suppressHistoricalMedicationSection)
            {
                var remainder =
                    GetTextAfterHistoricalMedicationSection(line);

                if (remainder is null)
                    continue;

                suppressHistoricalMedicationSection = false;
                line = remainder;

                if (line.Length == 0)
                    continue;
            }

            if (Regex.IsMatch(line.TrimStart(),
                    @"^(?:medications?\b|active outpatient medications\b|diagnos\w*\b|assessment\b|problem list\b)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                protectedClinicalSection = true;
            else if (line.TrimEnd().EndsWith(':'))
                protectedClinicalSection = false;
            if (!protectedClinicalSection)
                line = VeteransReviewerTypographicalCorrections.Apply(
                    block with { Text = line },
                    presentation.Corrections);

            if (!preformatted && TryParseReviewerField(
                    line,
                    out var fieldLabel,
                    out var fieldValue) &&
                ShouldRenderReviewerField(
                    fieldLabel,
                    fieldValue))
            {
                while (!presentation.IsBlueButton && lineIndex + 1 < layoutBlocks.Count &&
                       layoutBlocks[lineIndex + 1].Shape is not (VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow) &&
                       ShouldAppendReviewerFieldContinuation(
                           fieldValue,
                           layoutBlocks[lineIndex + 1].Text))
                {
                    fieldValue =
                        $"{fieldValue} {layoutBlocks[++lineIndex].Text}".Trim();
                }

                body.Append(
                    ReviewerFieldTable(
                        fieldLabel,
                        fieldValue));
                continue;
            }

            if (!preformatted && !presentation.IsBlueButton && IsReviewerListLine(line))
            {
                while (lineIndex + 1 < layoutBlocks.Count &&
                       layoutBlocks[lineIndex + 1].Shape is not (VeteransReviewerTextShape.Preformatted or VeteransReviewerTextShape.DataRow) &&
                       ShouldAppendReviewerListContinuation(
                           line,
                           layoutBlocks[lineIndex + 1].Text))
                {
                    line =
                        $"{line} {layoutBlocks[++lineIndex].Text}".Trim();
                }
            }

            var properties =
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        After = preformatted ? "0" :
                            IsReviewerStructuralLine(line)
                                ? "40"
                                : "60"
                    });

            properties.AddChild(new KeepLines(), true);

            if (!preformatted && !presentation.IsBlueButton && IsReviewerHeadingLine(line))
                properties.AddChild(new KeepNext(), true);

            body.Append(
                new Paragraph(
                    properties,
                    new Run(
                        new RunProperties(
                            new RunFonts
                            {
                                Ascii = preformatted ? VeteransReviewerFonts.Monospace : VeteransReviewerFonts.Body,
                                HighAnsi = preformatted ? VeteransReviewerFonts.Monospace : VeteransReviewerFonts.Body
                            },
                            new FontSize
                            {
                                Val = preformatted ? "18" : "24"
                            }),
                        new Text(SanitizeXmlText(line))
                        {
                            Space =
                                SpaceProcessingModeValues.Preserve
                        })));
        }
    }

    private static string ExpandReviewerTabs(string line)
    {
        var expanded = new StringBuilder();
        foreach (var character in line)
        {
            if (character == '\t')
                expanded.Append(' ', 8 - expanded.Length % 8);
            else
                expanded.Append(character);
        }
        return expanded.ToString();
    }

    private static bool TryParseReviewerField(
        string line,
        out string label,
        out string value)
    {
        label = string.Empty;
        value = string.Empty;

        if (!IsReviewerFieldLine(line))
            return false;

        var colon = line.IndexOf(':');

        label = line[..colon].Trim();
        value = line[(colon + 1)..].Trim();
        return label.Length > 0;
    }

    private static bool ShouldRenderReviewerField(
        string label,
        string value) =>
        value.Length > 0 ||
        label.Equals(
            "Requesting Provider",
            StringComparison.OrdinalIgnoreCase) ||
        label.Equals(
            "Requesting Physician",
            StringComparison.OrdinalIgnoreCase) ||
        label.Equals(
            "Referring Provider",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldAppendReviewerFieldContinuation(
        string currentValue,
        string nextLine) =>
        currentValue.Length > 0 &&
        nextLine.Length > 0 &&
        !IsReviewerStructuralLine(nextLine) &&
        !EndsReviewerSentence(currentValue);

    private static bool IsReviewerListLine(
        string line) =>
        line.StartsWith("•", StringComparison.Ordinal) ||
        line.StartsWith("- ", StringComparison.Ordinal) ||
        line.StartsWith("* ", StringComparison.Ordinal);

    private static bool ShouldAppendReviewerListContinuation(
        string currentLine,
        string nextLine) =>
        nextLine.Length > 0 &&
        !IsReviewerStructuralLine(nextLine) &&
        !EndsReviewerSentence(currentLine);

    private static bool EndsReviewerSentence(
        string text)
    {
        var value = text.TrimEnd();

        if (value.Length == 0)
            return false;

        return value[^1] is '.' or '!' or '?' or ':';
    }

    private static Table ReviewerFieldTable(
        string label,
        string value)
    {
        var table =
            new Table(
                new TableProperties(
                    new TableWidth
                    {
                        Type = TableWidthUnitValues.Pct,
                        Width = "5000"
                    },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Nil },
                        new LeftBorder { Val = BorderValues.Nil },
                        new BottomBorder { Val = BorderValues.Nil },
                        new RightBorder { Val = BorderValues.Nil },
                        new InsideHorizontalBorder { Val = BorderValues.Nil },
                        new InsideVerticalBorder { Val = BorderValues.Nil }),
                    new TableLayout { Type = TableLayoutValues.Fixed }));

        table.Append(
            new TableGrid(
                new GridColumn { Width = "2400" },
                new GridColumn { Width = "6200" }));

        table.Append(
            new TableRow(
                new TableRowProperties(
                    new CantSplit()),
                ReviewerFieldCell(
                    $"{label}:",
                    "1400",
                    bold: true),
                ReviewerFieldCell(
                    value,
                    "3600",
                    bold: false)));

        return table;
    }

    private static TableCell ReviewerFieldCell(
        string text,
        string width,
        bool bold)
    {
        var runProperties =
            new RunProperties(
                new RunFonts
                {
                    Ascii = VeteransReviewerFonts.Body,
                    HighAnsi = VeteransReviewerFonts.Body
                },
                new FontSize
                {
                    Val = "24"
                });

        if (bold)
            runProperties.AddChild(new Bold(), true);

        return new TableCell(
            new TableCellProperties(
                new TableCellWidth
                {
                    Type = TableWidthUnitValues.Pct,
                    Width = width
                },
                new TableCellVerticalAlignment
                {
                    Val = TableVerticalAlignmentValues.Top
                }),
            new Paragraph(
                new ParagraphProperties(
                    new KeepLines(),
                    new SpacingBetweenLines
                    {
                        After = "20"
                    }),
                new Run(
                    runProperties,
                    new Text(SanitizeXmlText(text))
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    })));
    }

    private static string? GetTextAfterHistoricalMedicationSection(
        string line)
    {
        var markers =
            new[]
            {
                "Columbia Suicide Severity Rating Scale",
                "C-SSRS",
                "PHYSICAL EXAM:",
                "ASSESSMENT/PLAN:",
                "REVIEW OF SYSTEMS",
                "ROS:",
                "OBJECTIVE:",
                "EXAM:",
                "PLAN:"
            };

        var earliestIndex = -1;

        foreach (var marker in markers)
        {
            var index =
                line.IndexOf(
                    marker,
                    StringComparison.OrdinalIgnoreCase);

            if (index >= 0 &&
                (earliestIndex < 0 || index < earliestIndex))
            {
                earliestIndex = index;
            }
        }

        return earliestIndex < 0
            ? null
            : line[earliestIndex..].Trim();
    }

    private static void AppendMedicalLiteratureText(
        Body body,
        string text)
    {
        var readableText =
            PrepareMedicalLiteratureText(text);

        foreach (var block in VeteransReviewerTextLayout.Project(readableText))
        {
            var preformatted = block.Shape == VeteransReviewerTextShape.Preformatted;
            var line = preformatted ? ExpandReviewerTabs(block.Text) : block.Text;
            if (line.Length == 0)
                continue;

            var heading =
                !preformatted && IsMedicalLiteratureHeadingLine(line);

            var properties =
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        Before = heading ? "160" : "0",
                        After = heading ? "80" : "120",
                        Line = heading ? "240" : "276",
                        LineRule = LineSpacingRuleValues.Auto
                    });

            if (heading)
            {
                properties.AddChild(new KeepLines(), true);
                properties.AddChild(new KeepNext(), true);
            }

            var runProperties =
                new RunProperties(
                    new RunFonts
                    {
                        Ascii = preformatted ? VeteransReviewerFonts.Monospace : VeteransReviewerFonts.Body,
                        HighAnsi = preformatted ? VeteransReviewerFonts.Monospace : VeteransReviewerFonts.Body
                    },
                    new FontSize
                    {
                        Val = preformatted ? "18" : "26"
                    });

            if (heading)
                runProperties.AddChild(new Bold(), true);

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

    private static string PrepareMedicalLiteratureText(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized =
            text
                .Replace('\uFFFD', ' ')
                .Trim();

        if (normalized.Contains('<', StringComparison.Ordinal) &&
            normalized.Contains('>', StringComparison.Ordinal))
        {
            try
            {
                var document =
                    XDocument.Parse(
                        normalized,
                        LoadOptions.PreserveWhitespace);

                var readable =
                    ExtractReadableMedicalLiteratureXml(document);

                if (!string.IsNullOrWhiteSpace(readable))
                    return readable;
            }
            catch (System.Xml.XmlException)
            {
                // Some literature sources contain incomplete XML fragments.
                // Fall through to conservative markup removal rather than
                // displaying XML/JATS tags to the physician.
            }
        }

        return normalized.Contains('<', StringComparison.Ordinal)
            ? StripResidualMedicalLiteratureMarkup(normalized)
            : normalized;
    }

    private static string ExtractReadableMedicalLiteratureXml(
        XDocument document)
    {
        var lines = new List<string>();
        var isPubMed =
            document.Root?.DescendantsAndSelf()
                .Any(
                    element =>
                        element.Name.LocalName is
                            "PubmedArticleSet" or "PubmedArticle") == true;

        if (isPubMed)
        {
            foreach (var abstractElement in
                document
                    .Descendants()
                    .Where(
                        element =>
                            element.Name.LocalName == "AbstractText"))
            {
                var label =
                    abstractElement.Attribute("Label")?.Value?.Trim();
                var value = NormalizeMedicalLiteratureInlineText(abstractElement.Value);

                if (value.Length == 0)
                    continue;

                if (!string.IsNullOrWhiteSpace(label))
                    lines.Add(label.TrimEnd(':'));

                lines.Add(value);
                lines.Add(string.Empty);
            }
        }
        else
        {
            foreach (var element in document.Descendants())
            {
                var localName = element.Name.LocalName;

                if (localName == "table")
                {
                    // The XML establishes row/cell order. Keep a literal tabbed
                    // transcription; do not infer spans or reconstruct a Word table.
                    foreach (var row in element.Descendants().Where(e => e.Name.LocalName == "tr"))
                    {
                        var cells = row.Elements().Where(e => e.Name.LocalName is "td" or "th")
                            .Select(e => NormalizeMedicalLiteratureInlineText(e.Value)).ToArray();
                        if (cells.Length > 0)
                            lines.Add(string.Join("\t", cells));
                    }
                    lines.Add(string.Empty);
                    continue;
                }

                if (localName is "fig" or "table-wrap")
                {
                    foreach (var caption in element.Elements().Where(e => e.Name.LocalName is "label" or "caption"))
                        lines.Add(NormalizeMedicalLiteratureInlineText(caption.Value));
                    continue;
                }


                if (localName is not ("title" or "p"))
                    continue;

                if (!element.Ancestors().Any(
                        ancestor =>
                            ancestor.Name.LocalName is "abstract" or "body"))
                {
                    continue;
                }

                if (element.Ancestors().Any(IsExcludedMedicalLiteratureXmlElement))
                    continue;

                var value =
                    NormalizeMedicalLiteratureInlineText(element.Value);

                if (value.Length == 0)
                    continue;

                lines.Add(value);

                if (localName == "p")
                    lines.Add(string.Empty);
            }
        }

        while (lines.Count > 0 &&
               lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    private static bool IsExcludedMedicalLiteratureXmlElement(
        XElement element) =>
        element.Name.LocalName is
            "ref-list" or
            "ref" or
            "table-wrap" or
            "table" or
            "fig" or
            "alternatives" or
            "graphic" or
            "permissions" or
            "custom-meta-group" or
            "supplementary-material" or
            "inline-supplementary-material";

    private static string NormalizeMedicalLiteratureInlineText(
        string text) =>
        Regex.Replace(
                WebUtility.HtmlDecode(text),
                @"\s+",
                " ",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
            .Trim();

    private static string StripResidualMedicalLiteratureMarkup(
        string text)
    {
        var cleaned =
            Regex.Replace(
                text,
                @"<\?.*?\?>",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        cleaned =
            Regex.Replace(
                cleaned,
                @"<[^>]+>",
                " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        cleaned =
            Regex.Replace(
                cleaned,
                "\\b(?:ref-type|rid|disp-level|position|content-type|rowspan|colspan|align|xlink:href|xmlns(?::xlink)?)\\s*=\\s*[\"'][^\"']*[\"']",
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        cleaned =
            Regex.Replace(
                cleaned,
                @"<(?=\d+\))",
                string.Empty,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        cleaned =
            WebUtility.HtmlDecode(cleaned);

        cleaned =
            Regex.Replace(
                cleaned,
                @"[ \t]+",
                " ",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));

        return cleaned.Trim();
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
                   StringComparison.OrdinalIgnoreCase) ||
               IsMedicalLiteratureAcronymHeading(heading);
    }

    private static bool IsMedicalLiteratureAcronymHeading(
        string heading)
    {
        if (heading.Length is < 2 or > 12 ||
            heading.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var letterCount = 0;

        foreach (var character in heading)
        {
            if (char.IsLetter(character))
            {
                letterCount++;

                if (!char.IsUpper(character))
                    return false;

                continue;
            }

            if (!char.IsDigit(character) &&
                character is not '-' and not '/' and not '&')
            {
                return false;
            }
        }

        return letterCount >= 2;
    }

    private static bool IsHistoricalMedicationSectionHeading(
        string line) =>
        line.Equals(
            "MEDICATIONS:",
            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith(
            "Active Outpatient Medications",
            StringComparison.OrdinalIgnoreCase);

    private static string? BuildHistoricalMedicationTitle(
        VeteransReviewerArtifactContent content)
    {
        if (!ContainsHistoricalMedicationSection(content))
            return null;

        var date = GetEvidenceDate(content);
        var formattedDate = date;

        if (DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            formattedDate =
                parsedDate.ToString(
                    "MMMM d, yyyy",
                    CultureInfo.InvariantCulture);
        }

        var displayName = GetDisplayName(content);
        var sourceType =
            ContainsReviewerText(displayName, "SLEEP")
                ? "VA Sleep Medicine Note"
                : "Source Record";

        return string.IsNullOrWhiteSpace(formattedDate)
            ? $"Historical Medication List — {sourceType}"
            : $"Historical Medication List — {formattedDate} {sourceType}";
    }

    private static bool ContainsHistoricalMedicationSection(
        VeteransReviewerArtifactContent content)
    {
        if (!string.Equals(
                content.Artifact.ArtifactType,
                "veterans-clinical-note",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(content.Text) &&
            NormalizeReviewerText(content.Text).Any(
                IsHistoricalMedicationSectionHeading))
        {
            return true;
        }

        foreach (var page in content.PrintablePages)
        {
            var geometry = page.TextGeometry;
            if (geometry is null)
                continue;

            var rows =
                geometry.Glyphs
                    .Where(glyph => !string.IsNullOrWhiteSpace(glyph.Text))
                    .GroupBy(glyph => Math.Round(glyph.Baseline, 1))
                    .OrderBy(group => group.Key)
                    .Select(group =>
                        string.Concat(
                            group
                                .OrderBy(glyph => glyph.X)
                                .Select(glyph => glyph.Text))
                            .Trim());

            if (rows.Any(IsHistoricalMedicationSectionHeading))
                return true;
        }

        return false;
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

        // A blank line inside a VA label/value block is commonly an extraction
        // artifact. Keep the fields in one compact block instead of rendering
        // a visible gap between adjacent metadata rows.
        for (var index = 1;
             index < output.Count - 1;)
        {
            if (output[index].Length == 0 &&
                IsReviewerFieldLine(output[index - 1]) &&
                IsReviewerFieldLine(output[index + 1]))
            {
                output.RemoveAt(index);
                continue;
            }

            index++;
        }

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

        var hasLetter = false;

        for (var index = 0;
             index < colon;
             index++)
        {
            var character = line[index];

            if (char.IsLetter(character))
            {
                hasLetter = true;
                continue;
            }

            if (char.IsDigit(character) ||
                char.IsWhiteSpace(character) ||
                character is '(' or ')' or '/' or '-' or '&' or '.' or '%')
            {
                continue;
            }

            return false;
        }

        // Prevent timestamps such as "03/18/2020 09: 43" from being
        // misread as a label/value field at the time colon.
        return hasLetter;
    }

    private static void AppendLiteratureSourcePages(
        MainDocumentPart mainPart, Body body,
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage> pages,
        VeteransReviewerEvidenceSections sections, bool sourcePageSelectionApplied)
    {
        var previousPageNumber = 0;
        // Keep blank-page notices with the article's provenance, so they do
        // not consume the space reserved for its full-size source images.
        foreach (var page in pages)
        {
            if (page.PageNumber <= previousPageNumber)
                throw new InvalidOperationException("Printable artifact pages must be in strictly increasing order.");
            if (!sourcePageSelectionApplied && page.PageNumber > previousPageNumber + 1)
            {
                var first = previousPageNumber + 1;
                var last = page.PageNumber - 1;
                body.Append(ContentParagraph(first == last
                    ? $"Source Page {first} was blank in the original document and is intentionally omitted from this reviewer copy."
                    : $"Source Pages {first}-{last} were blank in the original document and are intentionally omitted from this reviewer copy."));
            }
            previousPageNumber = page.PageNumber;
        }
        foreach (var page in pages)
        {
            var orientedContent = VeteransReviewerSourcePageOrientation.Orient(page);
            var (sourceWidth, sourceHeight) = GetPngDimensions(orientedContent);
            // The rasterizer has already applied the PDF's rotation. Its output
            // dimensions, rather than the unrotated MediaBox, determine layout.
            var landscape = sourceWidth > sourceHeight;
            var cropped = VeteransReviewerSourcePageCrop.Crop(orientedContent);
            var width = cropped.Width;
            var height = cropped.Height;
            sections.StartSourcePage(body, page.PageNumber, landscape);
            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
            using (var stream = new MemoryStream(cropped.Content.ToArray(), writable: false))
                imagePart.FeedData(stream);
            const long emusPerTwip = 635;
            var maxWidth = ((landscape ? 15840 : 12240) -
                2 * VeteransReviewerEvidenceSections.SourceSideMargin) * emusPerTwip;
            // Leave room for the inline drawing's paragraph baseline, not a
            // repeated body title or the introductory provenance paragraphs.
            var maxHeight = ((landscape ? 12240 : 15840) -
                VeteransReviewerEvidenceSections.SourceTopMargin -
                VeteransReviewerEvidenceSections.SourceBottomMargin - 80) * emusPerTwip;
            var scale = Math.Min(maxWidth / (double)width, maxHeight / (double)height);
            var paragraph = ImageParagraph(mainPart.GetIdOfPart(imagePart),
                checked((uint)mainPart.ImageParts.Count()), page.PageNumber,
                checked((long)Math.Round(width * scale)), checked((long)Math.Round(height * scale)));
            paragraph.ParagraphProperties!.AddChild(new SpacingBetweenLines { Before = "0", After = "0" }, true);
            paragraph.ParagraphProperties.AddChild(new Indentation { Left = "0", Right = "0", FirstLine = "0" }, true);
            var inline = paragraph.Descendants<DW.Inline>().Single();
            inline.DistanceFromLeft = 0U;
            inline.DistanceFromRight = 0U;
            inline.DistanceFromTop = 0U;
            inline.DistanceFromBottom = 0U;
            body.Append(paragraph);
        }
    }

    private static void AppendPrintablePages(
        MainDocumentPart mainPart,
        Body body,
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage> pages,
        IReadOnlyList<VeteransReviewerSourceClarification> clarifications,
        bool reviewerPageSelectionApplied,
        bool medicalLiterature,
        VeteransReviewerEvidencePresentation presentation,
        string? historicalMedicationTitle,
        string? historicalMedicationOmissionMessage,
        bool allowLargerSinglePageImage = false,
        bool medicalEvidence = false,
        string? artifactTitle = null,
        bool hasPackagePrescriptionList = false)
    {
        var previousPageNumber = 0;
        var renderedPageCount = 0;
        if (medicalEvidence && presentation.IsBlueButton && historicalMedicationTitle is not null)
        {
            pages = VeteransReviewerNativeEvidencePage.SuppressHistoricalMedications(pages, out var omittedRows);
            if (omittedRows > 0)
            {
                if (hasPackagePrescriptionList)
                    body.Append(ContentParagraph("See the separately dated VA Prescription List immediately preceding this note. " +
                        "The historical medication table within this note is excluded from the reviewer copy; the original source is preserved."));
                else
                {
                    body.Append(ContentParagraph(historicalMedicationTitle, keepWithNext: true));
                    body.Append(ContentParagraph(historicalMedicationOmissionMessage!));
                }
                reviewerPageSelectionApplied = true;
            }
        }
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
                if (!medicalEvidence) body.Append(
                    ContentParagraph(
                        $"Source Page {page.PageNumber}",
                        keepWithNext: true));

                var reviewerText =
                    ApplyReviewerSourceCorrections(
                        DecodePrintableText(page.Content),
                        clarifications);

                if (medicalLiterature)
                {
                    AppendMedicalLiteratureText(
                        body,
                        reviewerText);
                }
                else
                {
                    AppendReviewerText(
                        body,
                        reviewerText,
                        presentation, historicalMedicationTitle, historicalMedicationOmissionMessage);
                }

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

            var (sourceWidth, _) = GetPngDimensions(page.Content);
            var prepared = medicalEvidence
                ? VeteransReviewerNativeEvidencePage.Prepare(page, presentation.IsBlueButton, renderedPageCount == 0 ? artifactTitle : null)
                : new VeteransReviewerNativeEvidencePage(page.Content, []);
            var (width, height) = GetPngDimensions(prepared.Content);

            var imagePart =
                mainPart.AddImagePart(
                    ImagePartType.Png);

            using (var stream =
                new MemoryStream(
                    prepared.Content.ToArray(),
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
                        (!medicalEvidence && reviewerPageCount > 1),
                    allowLargerSinglePageImage:
                        allowLargerSinglePageImage &&
                        reviewerPageCount == 1,
                    medicalEvidence: medicalEvidence);

            if (medicalEvidence &&
                page.TextGeometry is { Width: > 0 } geometry &&
                sourceWidth > 0)
            {
                var sourceFontSizes =
                    geometry.Glyphs
                        .Where(glyph =>
                            !string.IsNullOrWhiteSpace(glyph.Text) &&
                            glyph.FontSize > 0)
                        .Select(glyph => glyph.FontSize)
                        .Order()
                        .ToArray();

                if (sourceFontSizes.Length > 0)
                {
                    const double emusPerPoint = 12_700d;
                    const double maxMedianDisplayedFontPoints = 10d;
                    var medianSourceFont =
                        sourceFontSizes[sourceFontSizes.Length / 2];
                    var displayedScale =
                        (cx / (double)width) /
                        (geometry.Width * emusPerPoint / sourceWidth);
                    var displayedMedianFont =
                        medianSourceFont * displayedScale;

                    // Cropping is not a reason to magnify a short source page.
                    // Preserve smaller 9–10 point originals and cap larger
                    // clinical type at 10 points with one uniform image scale.
                    var allowedMedianFont = Math.Min(medianSourceFont, maxMedianDisplayedFontPoints);
                    if (displayedMedianFont > allowedMedianFont)
                    {
                        var fontScale =
                            allowedMedianFont / displayedMedianFont;
                        cx = checked((long)Math.Round(cx * fontScale));
                        cy = checked((long)Math.Round(cy * fontScale));
                    }
                }
            }

            var drawingId =
                checked((uint)mainPart.ImageParts.Count());

            if (!medicalEvidence) body.Append(
                ContentParagraph(
                    $"Source Page {page.PageNumber}",
                    keepWithNext: true));

            var imageParagraph = ImageParagraph(
                    relationshipId,
                    drawingId,
                    page.PageNumber,
                    cx,
                    cy);
            if (medicalEvidence)
            {
                var inline = imageParagraph.Descendants<DW.Inline>().Single();
                inline.DistanceFromLeft = inline.DistanceFromRight = 0U;
                inline.DistanceFromTop = inline.DistanceFromBottom = 0U;
                inline.GetFirstChild<DW.DocProperties>()!.Description =
                    $"Native source page {page.PageNumber}. " + string.Join(" ", prepared.Changes);
                imageParagraph.ParagraphProperties!.Append(new Indentation { Left = "0", Right = "0", FirstLine = "0" });
            }
            body.Append(imageParagraph);

            previousPageNumber = page.PageNumber;
            renderedPageCount++;
        }
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
        bool reserveSourceHeadingSpace,
        bool allowLargerSinglePageImage = false,
        bool medicalEvidence = false,
        string? artifactTitle = null)
    {
        const long maxWidth = 5_943_600;
        const long standardMaxHeight = 7_772_400;
        const long firstSourcePageMaxHeight = 6_400_800;
        const long medicalFirstPageMaxHeight = 6_858_000; // 7.5 inches; no redundant source label.
        const long largerSinglePageMaxHeight = 7_000_000;

        var maxHeight =
            allowLargerSinglePageImage
                ? largerSinglePageMaxHeight
                : reserveSourceHeadingSpace
                    ? (medicalEvidence ? medicalFirstPageMaxHeight : firstSourcePageMaxHeight)
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

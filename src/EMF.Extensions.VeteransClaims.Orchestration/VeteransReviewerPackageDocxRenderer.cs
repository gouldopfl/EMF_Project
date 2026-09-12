using System.Buffers.Binary;
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

        foreach (var packageArtifact in
            details.PackageDetails.Artifacts)
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

        using var stream =
            new MemoryStream();

        using (var document =
            WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document))
        {
            var mainPart =
                document.AddMainDocumentPart();

            var body =
                new Body(
                    StyledParagraph(
                        "Veterans Evidence Reviewer Report",
                        "Title"),
                    StyledParagraph(
                        $"Claim Issue: {package.ClaimIssueId.Value}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Purpose: {package.Purpose}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Reviewer Role: {package.ReviewerRole}",
                        "Subtitle"),
                    StyledParagraph(
                        $"Package Reference: {package.Id.Value}",
                        "Subtitle"),
                    PageBreakParagraph());

            AppendExecutiveSummary(
                body,
                details);

            AppendReviewScope(
                body,
                details);

            AppendKeyEvidenceAndChronology(
                body,
                details);

            AppendMedicalLiteratureConsidered(
                body,
                details);

            AppendReviewerQuestions(
                body);

            AppendEvidenceIndex(
                body,
                details,
                pageBreakBefore: true);

            AppendEvidenceAppendices(
                mainPart,
                body,
                details);

            AppendTraceabilityAppendix(
                body,
                details);

            body.Append(
                new SectionProperties(
                    new PageMargin
                    {
                        Top = 1440,
                        Right = 1440U,
                        Bottom = 1440,
                        Left = 1440U
                    }));

            mainPart.Document =
                new Document(body);
        }

        return stream.ToArray();
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
        var contents =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.GeneratedOrganizationalMaterial);

        if (contents.Count == 0)
            return;

        body.Append(
            StyledParagraph(
                "Executive Evidence Summary",
                "Heading1"));

        foreach (var content in contents)
        {
            if (!string.IsNullOrWhiteSpace(content.Text))
            {
                AppendReviewerText(
                    body,
                    content.Text);
            }
        }
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
                $"Claim issue under review: {package.ClaimIssueId.Value}"));

        body.Append(
            ContentParagraph(
                $"Review purpose: {package.Purpose}"));

        body.Append(
            ContentParagraph(
                $"Reviewer role: {package.ReviewerRole}"));

        body.Append(
            ContentParagraph(
                $"Evidence sources supplied for review: {sourceCount}."));

        body.Append(
            ContentParagraph(
                "This report organizes evidence for independent medical review. " +
                "It does not make a medical, legal, or adjudicative conclusion."));
    }

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
                "date remain listed after the dated entries and in the Evidence Index."));

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

            if (!string.Equals(
                    displayName,
                    content.Artifact.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                body.Append(
                    ContentParagraph(
                        $"Source: {content.Artifact.Name}"));
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
                        $"Requirement: " +
                        $"{reviewed.Association.RequirementId.Value}"));

                body.Append(
                    ContentParagraph(
                        $"Role: {reviewed.Association.GuidanceRole}"));

                body.Append(
                    ContentParagraph(
                        $"Relevance: {reviewed.Association.Description}"));

                body.Append(
                    ContentParagraph(
                        $"Reviewed By: {reviewed.ReviewedBy}"));

                body.Append(
                    ContentParagraph(
                        $"Reviewed UTC: {reviewed.ReviewedUtc:O}"));

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

    private static void AppendEvidenceIndex(
        Body body,
        VeteransReviewerPackageDetails details,
        bool pageBreakBefore)
    {
        var contents =
            GetRoleContents(
                details,
                EvidencePackageContentRoles.UnderlyingEvidence);

        if (contents.Count == 0)
            return;

        body.Append(
            StyledParagraph(
                "Evidence Index",
                "Heading1",
                pageBreakBefore));

        body.Append(
            ContentParagraph(
                "The source evidence reviewed for this report is listed below. " +
                "Complete source material follows in the appendices."));

        var index = 1;

        foreach (var content in
            contents
                .OrderBy(content => AppendixOrder(content.Appendix ?? string.Empty))
                .ThenBy(
                    content =>
                        string.IsNullOrWhiteSpace(GetEvidenceDate(content))
                            ? 1
                            : 0)
                .ThenBy(content => GetEvidenceDate(content), StringComparer.Ordinal)
                .ThenBy(content => GetDisplayName(content), StringComparer.OrdinalIgnoreCase))
        {
            body.Append(
                StyledParagraph(
                    $"{index}. {GetDisplayName(content)}",
                    "Heading2"));

            body.Append(
                ContentParagraph(
                    BuildEvidenceIndexReference(content)));

            index++;
        }
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
                content,
                pageBreakBefore: !firstAdditionalArtifact);

            firstAdditionalArtifact = false;
        }
    }

    private static void AppendSourceContent(
        MainDocumentPart mainPart,
        Body body,
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

        if (!string.Equals(
                displayName,
                content.Artifact.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            body.Append(
                ContentParagraph(
                    $"Source: {content.Artifact.Name}",
                    keepWithNext: true));
        }

        if (content.PrintablePages.Count > 0)
        {
            AppendPrintablePages(
                mainPart,
                body,
                content.PrintablePages);
            return;
        }

        if (!string.IsNullOrWhiteSpace(content.Text))
        {
            AppendReviewerText(
                body,
                content.Text);
        }
    }

    private static void AppendTraceabilityAppendix(
        Body body,
        VeteransReviewerPackageDetails details)
    {
        if (details.ArtifactContents.Count == 0)
            return;

        body.Append(
            StyledParagraph(
                "Appendix F — Evidence Traceability",
                "Heading1",
                pageBreakBefore: true));

        body.Append(
            ContentParagraph(
                "This appendix preserves EMF artifact identity, integrity, " +
                "provenance, and relationship information separately from " +
                "the reviewer-facing source evidence."));

        foreach (var content in details.ArtifactContents)
        {
            var contentRole =
                details.PackageDetails.Artifacts
                    .First(
                        packageArtifact =>
                            packageArtifact.ArtifactId ==
                                content.Artifact.Id)
                    .ContentRole;

            body.Append(
                StyledParagraph(
                    $"Artifact Content: {content.Artifact.Name} " +
                    $"[{content.Artifact.Id.Value}] " +
                    $"[{contentRole}]",
                    "Heading2"));

            body.Append(
                ContentParagraph(
                    $"Artifact Type: {content.Artifact.ArtifactType}"));

            body.Append(
                ContentParagraph(
                    $"Created UTC: {content.Artifact.CreatedUtc:O}"));

            if (content.Artifact.Fingerprint is not null)
            {
                body.Append(
                    ContentParagraph(
                        $"Fingerprint: " +
                        $"{content.Artifact.Fingerprint.Algorithm} " +
                        $"{content.Artifact.Fingerprint.Value}"));
            }

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceStartPage,
                "Source Start Page");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.SourceEndPage,
                "Source End Page");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.NoteDate,
                "Note Date");

            AppendMetadata(
                body,
                content.Artifact.Metadata,
                EMF.Extensions.VeteransClaims.Models
                    .VeteransArtifactMetadataKeys.NoteTitle,
                "Note Title");

            foreach (var provenance in content.Provenance)
            {
                body.Append(
                    ContentParagraph(
                        $"Provenance: {provenance.Source} | " +
                        $"{provenance.RecordedBy} | " +
                        $"{provenance.RecordedUtc:O}"));
            }

            foreach (var provenance in content.Provenance)
            {
                if (!string.Equals(
                        provenance.Source,
                        "EMF.Intelligence",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                body.Append(
                    ContentParagraph(
                        $"Promoted By: {provenance.RecordedBy}"));

                body.Append(
                    ContentParagraph(
                        $"Promoted UTC: {provenance.RecordedUtc:O}"));

                if (provenance.Properties.TryGetValue(
                        "reviewedBy",
                        out var reviewedBy) &&
                    !string.IsNullOrWhiteSpace(reviewedBy?.ToString()))
                {
                    body.Append(
                        ContentParagraph(
                            $"Reviewed By: {reviewedBy}"));
                }

                if (provenance.Properties.TryGetValue(
                        "reviewedUtc",
                        out var reviewedUtc) &&
                    DateTimeOffset.TryParse(
                        reviewedUtc?.ToString(),
                        out var reviewedAt))
                {
                    body.Append(
                        ContentParagraph(
                            $"Reviewed UTC: {reviewedAt:O}"));
                }
            }

            foreach (var relationship in content.Relationships)
            {
                body.Append(
                    ContentParagraph(
                        $"Relationship: " +
                        $"{relationship.SourceArtifactId.Value} -> " +
                        $"{relationship.TargetArtifactId.Value} | " +
                        $"{relationship.RelationshipType} | " +
                        $"{relationship.CreatedUtc:O}"));
            }
        }
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

        return content.Artifact.Name;
    }

    private static string BuildEvidenceIndexReference(
        VeteransReviewerArtifactContent content)
    {
        var parts = new List<string>();

        if (content.Appendix is not null)
            parts.Add(AppendixHeading(content.Appendix));
        else
            parts.Add("Additional Evidence");

        var date = GetEvidenceDate(content);

        if (!string.IsNullOrWhiteSpace(date))
            parts.Add($"Date: {date}");

        var pages = GetSourcePageReference(content);

        if (!string.IsNullOrWhiteSpace(pages))
            parts.Add(pages);

        return string.Join(" | ", parts);
    }

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
                    Ascii = "Arial",
                    HighAnsi = "Arial"
                });

        if (string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "1F4E79" });
            properties.Append(new FontSize { Val = "36" });
        }
        else if (string.Equals(
                     styleId,
                     "Heading1",
                     StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "1F4E79" });
            properties.Append(new FontSize { Val = "28" });
        }
        else if (string.Equals(
                     styleId,
                     "Heading2",
                     StringComparison.Ordinal))
        {
            properties.Append(new Bold());
            properties.Append(new Color { Val = "365F91" });
            properties.Append(new FontSize { Val = "22" });
        }
        else if (string.Equals(
                     styleId,
                     "Subtitle",
                     StringComparison.Ordinal))
        {
            properties.Append(new Color { Val = "666666" });
            properties.Append(new FontSize { Val = "22" });
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
                        Ascii = "Arial",
                        HighAnsi = "Arial"
                    },
                    new FontSize
                    {
                        Val = "20"
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
                                Ascii = "Arial",
                                HighAnsi = "Arial"
                            },
                            new FontSize
                            {
                                Val = "20"
                            }),
                        new Text(SanitizeXmlText(line))
                        {
                            Space =
                                SpaceProcessingModeValues.Preserve
                        })));
        }
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
        IReadOnlyList<EMF.Core.Models.PrintableArtifactPage> pages)
    {
        var expectedPageNumber = 1;

        foreach (var page in pages)
        {
            if (page.PageNumber != expectedPageNumber)
                throw new InvalidOperationException(
                    "Printable artifact pages are not in sequential order.");

            if (expectedPageNumber > 1)
                body.Append(PageBreakParagraph());

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
                expectedPageNumber++;
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
                FitPageToDocument(width, height);

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

            expectedPageNumber++;
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
        uint height)
    {
        const long maxWidth = 5_943_600;
        const long maxHeight = 7_772_400;

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

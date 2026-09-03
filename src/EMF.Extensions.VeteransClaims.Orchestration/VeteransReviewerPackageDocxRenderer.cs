using DocumentFormat.OpenXml;
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

        using var stream =
            new MemoryStream();

        using (var document =
            WordprocessingDocument.Create(
                stream,
                WordprocessingDocumentType.Document))
        {
            var mainPart =
                document.AddMainDocumentPart();

            var package =
                details.PackageDetails.Package;

            var body =
                new Body(
                    StyledParagraph(
                        "Veterans Evidence Reviewer Package",
                        "Title"),
                    Paragraph(
                        $"Package: {package.Id.Value}"),
                    Paragraph(
                        $"Claim Issue: {package.ClaimIssueId.Value}"),
                    Paragraph(
                        $"Purpose: {package.Purpose}"),
                    Paragraph(
                        $"Reviewer Role: {package.ReviewerRole}"));

            AppendRoleSection(
                body,
                details,
                EvidencePackageContentRoles
                    .GeneratedOrganizationalMaterial,
                "Generated Organizational Material");

            AppendRoleSection(
                body,
                details,
                EvidencePackageContentRoles
                    .UnderlyingEvidence,
                "Underlying Evidence");

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

    private static void AppendRoleSection(
        Body body,
        VeteransReviewerPackageDetails details,
        string contentRole,
        string heading)
    {
        var contents =
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

        if (contents.Length == 0)
            return;

        body.Append(
            StyledParagraph(
                heading,
                "Heading1"));

        foreach (var content in contents)
        {
            body.Append(
                StyledParagraph(
                    $"Artifact Content: {content.Artifact.Name} " +
                    $"[{content.Artifact.Id.Value}] " +
                    $"[{contentRole}]",
                    "Heading2"));

            body.Append(
                Paragraph(content.Text));
        }
    }

    private static Paragraph StyledParagraph(
        string text,
        string styleId)
    {
        var properties =
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = styleId
                });

        if (string.Equals(
                styleId,
                "Title",
                StringComparison.Ordinal))
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    After = "240"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading1",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "120"
                });
        }
        else if (string.Equals(
                     styleId,
                     "Heading2",
                     StringComparison.Ordinal))
        {
            properties.Append(
                new SpacingBetweenLines
                {
                    Before = "120",
                    After = "60"
                });
        }

        return new Paragraph(
            properties,
            new Run(
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
    }

    private static Paragraph Paragraph(
        string text) =>
        new(
            new Run(
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
}

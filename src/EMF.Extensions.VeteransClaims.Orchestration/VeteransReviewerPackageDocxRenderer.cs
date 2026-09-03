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
                Paragraph(
                    $"Artifact Content: {content.Artifact.Name} " +
                    $"[{content.Artifact.Id.Value}] " +
                    $"[{contentRole}]"));

            body.Append(
                Paragraph(content.Text));
        }
    }

    private static Paragraph StyledParagraph(
        string text,
        string styleId) =>
        new(
            new ParagraphProperties(
                new ParagraphStyleId
                {
                    Val = styleId
                }),
            new Run(
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));

    private static Paragraph Paragraph(
        string text) =>
        new(
            new Run(
                new Text(text)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
}

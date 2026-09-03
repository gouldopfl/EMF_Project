using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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
                    Paragraph(
                        $"Package: {package.Id.Value}"),
                    Paragraph(
                        $"Claim Issue: {package.ClaimIssueId.Value}"),
                    Paragraph(
                        $"Purpose: {package.Purpose}"),
                    Paragraph(
                        $"Reviewer Role: {package.ReviewerRole}"));

            foreach (var content in details.ArtifactContents)
            {
                body.Append(
                    Paragraph(
                        $"Artifact Content: {content.Artifact.Name} " +
                        $"[{content.Artifact.Id.Value}]"));

                body.Append(
                    Paragraph(content.Text));
            }

            mainPart.Document =
                new Document(body);
        }

        return stream.ToArray();
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

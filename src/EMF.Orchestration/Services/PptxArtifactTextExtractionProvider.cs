using System.Text;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class PptxArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly IArtifactContentStore _contentStore;

    public PptxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            ContentType,
            StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        using var stream =
            new MemoryStream(content, writable: false);

        using var document =
            PresentationDocument.Open(stream, false);

        var presentationPart =
            document.PresentationPart;

        var slideIds =
            presentationPart?
                .Presentation?
                .SlideIdList;

        if (presentationPart is null ||
            slideIds is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var slideNumber = 0;

        foreach (var slideId in slideIds.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId =
                slideId.GetAttribute("id",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
                    .Value;

            if (string.IsNullOrWhiteSpace(relationshipId))
                continue;

            if (presentationPart.GetPartById(relationshipId)
                is not SlidePart slidePart)
            {
                continue;
            }

            slideNumber++;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.AppendLine(
                $"[Slide {slideNumber}]");

            var slide =
                slidePart.Slide;

            if (slide is null)
                continue;

            foreach (var text in
                slide.Descendants<Text>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(text.Text))
                    continue;

                builder.AppendLine(text.Text);
            }
        }

        return builder.ToString().TrimEnd();
    }
}

using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class HtmlArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider
{
    private readonly HtmlArtifactTextExtractionProvider _extractor;

    public HtmlArtifactPrintRenderingProvider(
        HtmlArtifactTextExtractionProvider extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _extractor = extractor;
    }

    public bool CanRender(string contentType) =>
        string.Equals(
            contentType,
            "text/html",
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var text =
            await _extractor.ExtractTextAsync(
                artifactId,
                cancellationToken);

        if (text is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        return
        [
            new PrintableArtifactPage
            {
                PageNumber = 1,
                ContentType = "text/plain",
                Content = Encoding.UTF8.GetBytes(text)
            }
        ];
    }
}

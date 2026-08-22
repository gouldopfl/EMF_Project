using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EMF.Orchestration.Services;

public sealed class PdfArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;

    public PdfArtifactTextExtractionProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            "application/pdf",
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

        using var document =
            PdfDocument.Open(content);

        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(
                ContentOrderTextExtractor.GetText(page));
        }

        return builder.ToString();
    }
}

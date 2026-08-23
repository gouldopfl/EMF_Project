using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ImageArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private readonly IArtifactContentStore _contentStore;
    private readonly IImageOcrService _ocrService;

    public ImageArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IImageOcrService ocrService)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(ocrService);

        _contentStore = contentStore;
        _ocrService = ocrService;
    }

    public bool CanExtract(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/tiff", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

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

        return await _ocrService.RecognizeTextAsync(
            new OcrRequest(content),
            cancellationToken);
    }
}

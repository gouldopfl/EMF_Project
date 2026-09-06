using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class ImageArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const long DefaultMaxInputBytes = 100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars = 10 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly IImageOcrService _ocrService;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public ImageArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IImageOcrService ocrService,
        long maxInputBytes = DefaultMaxInputBytes,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(ocrService);

        if (maxInputBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));

        if (maxExtractedTextChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _ocrService = ocrService;
        _maxInputBytes = maxInputBytes;
        _maxExtractedTextChars = maxExtractedTextChars;
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

        if (content.LongLength > _maxInputBytes)
            throw new InvalidDataException(
                "Image input exceeds the maximum allowed size.");

        var text =
            await _ocrService.RecognizeTextAsync(
                new OcrRequest(content),
                cancellationToken);

        if (text is not null &&
            text.Length > _maxExtractedTextChars)
            throw new InvalidDataException(
                "Image OCR text exceeds the maximum allowed size.");

        return text;
    }
}

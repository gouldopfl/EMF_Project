using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Reader;
using OfficeIMO.Reader.PowerPoint;

namespace EMF.Orchestration.Services;

public sealed class PptArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.ms-powerpoint";

    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly OfficeDocumentReader _reader;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public PptArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes =
            DefaultMaxInputBytes,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ValidatePositive(
            maxInputBytes,
            nameof(maxInputBytes));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxInputBytes = maxInputBytes;
        _maxExtractedTextChars = maxExtractedTextChars;
        _reader =
            new OfficeDocumentReaderBuilder()
                .AddPowerPointHandler()
                .Build();
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

        if (content.LongLength > _maxInputBytes)
        {
            throw new InvalidDataException(
                "PPT input exceeds the maximum allowed size.");
        }

        var result =
            await _reader.ReadDocumentAsync(
                content,
                "evidence.ppt",
                options: null,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var markdown = result.Markdown;

        if (markdown is not null &&
            markdown.Length > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "PPT extracted text exceeds " +
                "the maximum allowed size.");
        }

        return markdown;
    }

    private static void ValidatePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Resource limit must be greater than zero.");
        }
    }
}

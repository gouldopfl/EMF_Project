using b2xtranslator.DocFileFormat;
using b2xtranslator.StructuredStorage.Reader;
using b2xtranslator.txt;
using b2xtranslator.txt.TextModel;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class DocArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/msword";

    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public DocArtifactTextExtractionProvider(
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
                "DOC input exceeds the maximum allowed size.");
        }

        using var stream =
            new MemoryStream(content, writable: false);

        using var reader =
            new StructuredStorageReader(
                stream,
                logger: null);

        var document =
            new WordDocument(
                reader,
                fibFC: 0);

        var textDocument =
            TextDocument.Create(
                string.Empty,
                writer: null!,
                extractUrls: true);

        cancellationToken.ThrowIfCancellationRequested();

        var text =
            DocTextExtractor.ConvertToString(
                document,
                textDocument,
                extractUrls: true);

        cancellationToken.ThrowIfCancellationRequested();

        if (text.Length > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "DOC extracted text exceeds " +
                "the maximum allowed size.");
        }

        return text;
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

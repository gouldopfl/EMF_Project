using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;

namespace EMF.Orchestration.Services;

public sealed class MsgArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.ms-outlook";

    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly IOutlookMessageDecoder _decoder;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public MsgArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        IOutlookMessageDecoder decoder,
        long maxInputBytes = DefaultMaxInputBytes,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(decoder);
        ValidatePositive(
            maxInputBytes,
            nameof(maxInputBytes));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _decoder = decoder;
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
                "Outlook MSG input exceeds the maximum allowed size.");
        }

        var message =
            await _decoder.DecodeAsync(
                content,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var text =
            !string.IsNullOrWhiteSpace(message.BodyText)
                ? message.BodyText
                : message.BodyHtml;

        if (text is not null &&
            text.Length > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "Outlook MSG extracted text exceeds " +
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

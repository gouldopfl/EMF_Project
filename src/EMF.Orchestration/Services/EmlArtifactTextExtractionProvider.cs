using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class EmlArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const long DefaultMaxInputBytes =
        150L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public EmlArtifactTextExtractionProvider(
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
            "message/rfc822",
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
                "EML input exceeds the maximum allowed size.");
        }

        var encoding =
            new UTF8Encoding(
                false,
                true);

        _ = encoding.GetCharCount(content);

        cancellationToken.ThrowIfCancellationRequested();

        var separator =
            content.AsSpan().IndexOf(
                "\r\n\r\n"u8);

        var separatorLength = 4;

        if (separator < 0)
        {
            separator =
                content.AsSpan().IndexOf(
                    "\n\n"u8);

            separatorLength = 2;
        }

        if (separator < 0)
        {
            throw new FormatException(
                "The EML message does not contain a header/body separator.");
        }

        var body =
            content.AsSpan(
                separator + separatorLength);

        var bodyCharCount =
            encoding.GetCharCount(body);

        if (bodyCharCount > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "EML message body exceeds " +
                "the maximum allowed size.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return encoding.GetString(body);
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

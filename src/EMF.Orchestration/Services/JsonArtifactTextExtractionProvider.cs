using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class JsonArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxExtractedTextChars;

    public JsonArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes = DefaultMaxInputBytes,
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
            "application/json",
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
                "JSON input exceeds the maximum allowed size.");
        }

        var encoding =
            new UTF8Encoding(
                false,
                true);

        var characterCount =
            encoding.GetCharCount(content);

        if (characterCount > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "JSON extracted text exceeds " +
                "the maximum allowed size.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return encoding.GetString(content);
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

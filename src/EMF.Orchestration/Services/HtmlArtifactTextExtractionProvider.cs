using System.Net;
using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using MimeKit.Text;

namespace EMF.Orchestration.Services;

public sealed class HtmlArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxTokenCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxTokenCount;
    private readonly int _maxExtractedTextChars;

    public HtmlArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes =
            DefaultMaxInputBytes,
        int maxTokenCount =
            DefaultMaxTokenCount,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ValidatePositive(
            maxInputBytes,
            nameof(maxInputBytes));
        ValidatePositive(
            maxTokenCount,
            nameof(maxTokenCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxInputBytes = maxInputBytes;
        _maxTokenCount = maxTokenCount;
        _maxExtractedTextChars = maxExtractedTextChars;
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            "text/html",
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
                "HTML input exceeds the maximum allowed size.");
        }

        using var stream =
            new MemoryStream(content, writable: false);

        var tokenizer =
            new HtmlTokenizer(stream, Encoding.UTF8);

        var builder = new StringBuilder();
        var suppressed = false;
        var tokenCount = 0;

        while (tokenizer.ReadNextToken(out var token))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tokenCount >= _maxTokenCount)
            {
                throw new InvalidDataException(
                    "HTML input exceeds the maximum " +
                    "allowed token count.");
            }

            tokenCount++;

            if (token is HtmlTagToken tag)
            {
                if (tag.Name.Equals("script",
                        StringComparison.OrdinalIgnoreCase) ||
                    tag.Name.Equals("style",
                        StringComparison.OrdinalIgnoreCase))
                {
                    suppressed = !tag.IsEndTag;
                }

                continue;
            }

            if (!suppressed &&
                token is HtmlDataToken data)
            {
                AppendBounded(
                    builder,
                    WebUtility.HtmlDecode(data.Data));
            }
        }

        return builder.ToString().Trim();
    }

    private void AppendBounded(
        StringBuilder builder,
        string value)
    {
        if (value.Length >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "HTML extracted text exceeds " +
                "the maximum allowed size.");
        }

        builder.Append(value);
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

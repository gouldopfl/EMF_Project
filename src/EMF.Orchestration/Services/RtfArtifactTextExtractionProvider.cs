using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Rtf;

namespace EMF.Orchestration.Services;

public sealed class RtfArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/rtf";

    public const long DefaultMaxInputBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxParagraphCount = 1_000_000;
    public const int DefaultMaxRunCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly long _maxInputBytes;
    private readonly int _maxParagraphCount;
    private readonly int _maxRunCount;
    private readonly int _maxExtractedTextChars;

    public RtfArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        long maxInputBytes =
            DefaultMaxInputBytes,
        int maxParagraphCount =
            DefaultMaxParagraphCount,
        int maxRunCount =
            DefaultMaxRunCount,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ValidatePositive(
            maxInputBytes,
            nameof(maxInputBytes));
        ValidatePositive(
            maxParagraphCount,
            nameof(maxParagraphCount));
        ValidatePositive(
            maxRunCount,
            nameof(maxRunCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxInputBytes = maxInputBytes;
        _maxParagraphCount = maxParagraphCount;
        _maxRunCount = maxRunCount;
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
                "RTF input exceeds the maximum allowed size.");
        }

        var result =
            RtfDocument.Load(
                content,
                options: null);

        var builder = new StringBuilder();
        var paragraphCount = 0;
        var runCount = 0;

        foreach (var paragraph in result.Document.Paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (paragraphCount >= _maxParagraphCount)
            {
                throw new InvalidDataException(
                    "RTF document exceeds the maximum " +
                    "allowed paragraph count.");
            }

            paragraphCount++;

            foreach (var run in paragraph.Runs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (runCount >= _maxRunCount)
                {
                    throw new InvalidDataException(
                        "RTF document exceeds the maximum " +
                        "allowed text run count.");
                }

                runCount++;

                AppendBounded(
                    builder,
                    run.Text ?? string.Empty);
            }

            AppendBounded(
                builder,
                Environment.NewLine);
        }

        return builder.ToString().TrimEnd();
    }

    private void AppendBounded(
        StringBuilder builder,
        string value)
    {
        if (value.Length >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "RTF extracted text exceeds " +
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

using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class PptxArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public const int DefaultMaxPackageEntryCount = 1000;
    public const long DefaultMaxPackageEntryBytes = 25L * 1024 * 1024;
    public const long DefaultMaxPackageTotalBytes = 100L * 1024 * 1024;
    public const int DefaultMaxSlideCount = 1000;
    public const int DefaultMaxTextNodeCount = 1_000_000;
    public const int DefaultMaxExtractedTextChars = 10 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly int _maxPackageEntryCount;
    private readonly long _maxPackageEntryBytes;
    private readonly long _maxPackageTotalBytes;
    private readonly int _maxSlideCount;
    private readonly int _maxTextNodeCount;
    private readonly int _maxExtractedTextChars;

    public PptxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        int maxPackageEntryCount = DefaultMaxPackageEntryCount,
        long maxPackageEntryBytes = DefaultMaxPackageEntryBytes,
        long maxPackageTotalBytes = DefaultMaxPackageTotalBytes,
        int maxSlideCount = DefaultMaxSlideCount,
        int maxTextNodeCount = DefaultMaxTextNodeCount,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        ValidatePositive(
            maxPackageEntryCount,
            nameof(maxPackageEntryCount));
        ValidatePositive(
            maxPackageEntryBytes,
            nameof(maxPackageEntryBytes));
        ValidatePositive(
            maxPackageTotalBytes,
            nameof(maxPackageTotalBytes));
        ValidatePositive(
            maxSlideCount,
            nameof(maxSlideCount));
        ValidatePositive(
            maxTextNodeCount,
            nameof(maxTextNodeCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxPackageEntryCount = maxPackageEntryCount;
        _maxPackageEntryBytes = maxPackageEntryBytes;
        _maxPackageTotalBytes = maxPackageTotalBytes;
        _maxSlideCount = maxSlideCount;
        _maxTextNodeCount = maxTextNodeCount;
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

        ValidatePackage(content);

        using var stream =
            new MemoryStream(content, writable: false);

        using var document =
            PresentationDocument.Open(stream, false);

        var presentationPart =
            document.PresentationPart;

        var slideIds =
            presentationPart?
                .Presentation?
                .SlideIdList;

        if (presentationPart is null ||
            slideIds is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var slideNumber = 0;
        var textNodeCount = 0;

        foreach (var slideId in slideIds.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationshipId =
                slideId.GetAttribute("id",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
                    .Value;

            if (string.IsNullOrWhiteSpace(relationshipId))
                continue;

            if (presentationPart.GetPartById(relationshipId)
                is not SlidePart slidePart)
            {
                continue;
            }

            slideNumber++;

            if (slideNumber > _maxSlideCount)
            {
                throw new InvalidDataException(
                    "PPTX exceeds the maximum allowed slide count.");
            }

            if (builder.Length > 0)
                AppendLine(builder, string.Empty);

            AppendLine(
                builder,
                $"[Slide {slideNumber}]");

            var slide =
                slidePart.Slide;

            if (slide is null)
                continue;

            foreach (var text in
                slide.Descendants<Text>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                textNodeCount++;

                if (textNodeCount > _maxTextNodeCount)
                {
                    throw new InvalidDataException(
                        "PPTX exceeds the maximum allowed text-node count.");
                }

                if (string.IsNullOrWhiteSpace(text.Text))
                    continue;

                AppendLine(
                    builder,
                    text.Text);
            }
        }

        return builder.ToString().TrimEnd();
    }
    private void AppendLine(
        StringBuilder builder,
        string value)
    {
        var requiredCharacters =
            value.Length +
            Environment.NewLine.Length;

        if (requiredCharacters >
            _maxExtractedTextChars - builder.Length)
        {
            throw new InvalidDataException(
                "PPTX extracted text exceeds the maximum allowed size.");
        }

        builder.AppendLine(value);
    }

    private static void ValidatePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "PPTX extraction limits must be positive.");
        }
    }

    private void ValidatePackage(byte[] content)
    {
        using var stream =
            new MemoryStream(content, writable: false);

        using var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

        if (archive.Entries.Count > _maxPackageEntryCount)
        {
            throw new InvalidDataException(
                "PPTX package exceeds the maximum allowed entry count.");
        }

        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Length > _maxPackageEntryBytes)
            {
                throw new InvalidDataException(
                    "PPTX package entry exceeds the maximum allowed size.");
            }

            if (entry.Length >
                _maxPackageTotalBytes - totalBytes)
            {
                throw new InvalidDataException(
                    "PPTX package exceeds the maximum allowed extracted size.");
            }

            totalBytes += entry.Length;
        }
    }

}

using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Services;

public sealed class DocxArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    private const string ContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public const int DefaultMaxPackageEntryCount = 1000;
    public const long DefaultMaxPackageEntryBytes = 25L * 1024 * 1024;
    public const long DefaultMaxPackageTotalBytes = 100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars = 10 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly int _maxPackageEntryCount;
    private readonly long _maxPackageEntryBytes;
    private readonly long _maxPackageTotalBytes;
    private readonly int _maxExtractedTextChars;

    public DocxArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        int maxPackageEntryCount = DefaultMaxPackageEntryCount,
        long maxPackageEntryBytes = DefaultMaxPackageEntryBytes,
        long maxPackageTotalBytes = DefaultMaxPackageTotalBytes,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        if (maxPackageEntryCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxPackageEntryCount));

        if (maxPackageEntryBytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxPackageEntryBytes));

        if (maxPackageTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxPackageTotalBytes));

        if (maxExtractedTextChars <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _maxPackageEntryCount = maxPackageEntryCount;
        _maxPackageEntryBytes = maxPackageEntryBytes;
        _maxPackageTotalBytes = maxPackageTotalBytes;
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
            WordprocessingDocument.Open(stream, false);

        var body =
            document.MainDocumentPart?
                .Document?
                .Body;

        if (body is null)
            return string.Empty;

        var builder = new StringBuilder();

        foreach (var node in body.Descendants<Text>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var value = node.Text;

            if (string.IsNullOrEmpty(value))
                continue;

            if (value.Length >
                _maxExtractedTextChars - builder.Length)
            {
                throw new InvalidDataException(
                    "DOCX extracted text exceeds the maximum allowed size.");
            }

            builder.Append(value);
        }

        return builder.ToString();
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
                "DOCX package exceeds the maximum allowed entry count.");
        }

        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Length > _maxPackageEntryBytes)
            {
                throw new InvalidDataException(
                    "DOCX package entry exceeds the maximum allowed size.");
            }

            if (entry.Length >
                _maxPackageTotalBytes - totalBytes)
            {
                throw new InvalidDataException(
                    "DOCX package exceeds the maximum allowed extracted size.");
            }

            totalBytes += entry.Length;
        }
    }

}

using System.IO.Compression;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using OfficeIMO.Reader;
using OfficeIMO.Reader.OpenDocument;

namespace EMF.Orchestration.Services;

public sealed class OpenDocumentArtifactTextExtractionProvider :
    IArtifactTextExtractionProvider
{
    public const int DefaultMaxPackageEntryCount = 1_000;
    public const long DefaultMaxPackageEntryBytes =
        25L * 1024 * 1024;
    public const long DefaultMaxPackageTotalBytes =
        100L * 1024 * 1024;
    public const int DefaultMaxExtractedTextChars =
        50 * 1024 * 1024;

    private readonly IArtifactContentStore _contentStore;
    private readonly OfficeDocumentReader _reader;
    private readonly string _contentType;
    private readonly string _fileName;
    private readonly int _maxPackageEntryCount;
    private readonly long _maxPackageEntryBytes;
    private readonly long _maxPackageTotalBytes;
    private readonly int _maxExtractedTextChars;

    public OpenDocumentArtifactTextExtractionProvider(
        IArtifactContentStore contentStore,
        string contentType,
        string fileName,
        int maxPackageEntryCount =
            DefaultMaxPackageEntryCount,
        long maxPackageEntryBytes =
            DefaultMaxPackageEntryBytes,
        long maxPackageTotalBytes =
            DefaultMaxPackageTotalBytes,
        int maxExtractedTextChars =
            DefaultMaxExtractedTextChars)
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
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _contentStore = contentStore;
        _contentType = contentType;
        _fileName = fileName;
        _maxPackageEntryCount = maxPackageEntryCount;
        _maxPackageEntryBytes = maxPackageEntryBytes;
        _maxPackageTotalBytes = maxPackageTotalBytes;
        _maxExtractedTextChars = maxExtractedTextChars;

        _reader =
            new OfficeDocumentReaderBuilder()
                .AddOpenDocumentHandler()
                .Build();
    }

    public bool CanExtract(string contentType) =>
        string.Equals(
            contentType,
            _contentType,
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

        ValidatePackage(
            content,
            cancellationToken);

        var result =
            _reader.ReadDocument(
                content,
                _fileName,
                options: null,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var markdown = result.Markdown;

        if (markdown is not null &&
            markdown.Length > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "OpenDocument extracted text exceeds " +
                "the maximum allowed size.");
        }

        return markdown;
    }

    private void ValidatePackage(
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var stream =
            new MemoryStream(
                content,
                writable: false);

        using var archive =
            new ZipArchive(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

        if (archive.Entries.Count > _maxPackageEntryCount)
        {
            throw new InvalidDataException(
                "OpenDocument package exceeds " +
                "the maximum allowed entry count.");
        }

        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryBytes = entry.Length;

            if (entryBytes > _maxPackageEntryBytes)
            {
                throw new InvalidDataException(
                    "OpenDocument package entry exceeds " +
                    "the maximum allowed size.");
            }

            if (entryBytes >
                _maxPackageTotalBytes - totalBytes)
            {
                throw new InvalidDataException(
                    "OpenDocument package exceeds " +
                    "the maximum allowed extracted size.");
            }

            totalBytes += entryBytes;
        }
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

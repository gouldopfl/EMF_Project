using System.IO.Compression;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipArchiveDecoder :
    IZipArchiveDecoder
{
    public const int DefaultMaxEntryCount = 1000;
    public const long DefaultMaxEntryBytes = 25L * 1024 * 1024;
    public const long DefaultMaxTotalBytes = 100L * 1024 * 1024;

    private readonly int _maxEntryCount;
    private readonly long _maxEntryBytes;
    private readonly long _maxTotalBytes;

    public ZipArchiveDecoder(
        int maxEntryCount = DefaultMaxEntryCount,
        long maxEntryBytes = DefaultMaxEntryBytes,
        long maxTotalBytes = DefaultMaxTotalBytes)
    {
        if (maxEntryCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxEntryCount));

        if (maxEntryBytes <= 0 ||
            maxEntryBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEntryBytes));
        }

        if (maxTotalBytes <= 0 ||
            maxTotalBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTotalBytes));
        }

        _maxEntryCount = maxEntryCount;
        _maxEntryBytes = maxEntryBytes;
        _maxTotalBytes = maxTotalBytes;
    }

    public async Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var stream =
            new MemoryStream(content.ToArray(), writable: false);

        using var archive =
            new ZipArchive(stream, ZipArchiveMode.Read);

        if (archive.Entries.Count > _maxEntryCount)
        {
            throw new InvalidDataException(
                "ZIP archive exceeds the maximum allowed entry count.");
        }

        var results = new List<DecodedArchiveEntry>();
        long declaredTotalBytes = 0;
        long extractedTotalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            if (entry.Length > _maxEntryBytes)
                throw new InvalidDataException(
                    $"ZIP entry '{entry.FullName}' exceeds the maximum allowed size.");

            declaredTotalBytes += entry.Length;

            if (declaredTotalBytes > _maxTotalBytes)
            {
                throw new InvalidDataException(
                    "ZIP archive exceeds the maximum allowed extracted size.");
            }

            await using var entryStream = entry.Open();
            using var output = new MemoryStream();

            var buffer = new byte[81920];
            long entryBytes = 0;

            while (true)
            {
                var read =
                    await entryStream.ReadAsync(
                        buffer.AsMemory(),
                        cancellationToken);

                if (read == 0)
                    break;

                entryBytes += read;
                extractedTotalBytes += read;

                if (entryBytes > _maxEntryBytes)
                {
                    throw new InvalidDataException(
                        $"ZIP entry '{entry.FullName}' exceeds the maximum allowed size.");
                }

                if (extractedTotalBytes > _maxTotalBytes)
                {
                    throw new InvalidDataException(
                        "ZIP archive exceeds the maximum allowed extracted size.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            results.Add(
                new DecodedArchiveEntry
                {
                    EntryName = entry.FullName,
                    Content = output.ToArray()
                });
        }

        return results;
    }
}

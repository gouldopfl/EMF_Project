using System.IO.Compression;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipArchiveDecoder :
    IZipArchiveDecoder
{
    private const long MaxEntryBytes = 25 * 1024 * 1024;
    private const long MaxTotalBytes = 100 * 1024 * 1024;

    public async Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        using var stream =
            new MemoryStream(content.ToArray(), writable: false);

        using var archive =
            new ZipArchive(stream, ZipArchiveMode.Read);

        var results = new List<DecodedArchiveEntry>();
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            if (entry.Length > MaxEntryBytes)
                throw new InvalidDataException(
                    $"ZIP entry '{entry.FullName}' exceeds the maximum allowed size.");

            totalBytes += entry.Length;

            if (totalBytes > MaxTotalBytes)
                throw new InvalidDataException(
                    "ZIP archive exceeds the maximum allowed extracted size.");

            await using var entryStream = entry.Open();
            using var output = new MemoryStream();

            await entryStream.CopyToAsync(
                output,
                cancellationToken);

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

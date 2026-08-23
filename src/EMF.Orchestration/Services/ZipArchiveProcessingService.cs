using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ZipArchiveProcessingService :
    IZipArchiveProcessingService
{
    private readonly IZipArchiveDecoder _decoder;
    private readonly IZipEntryExtractionService _extractionService;

    public ZipArchiveProcessingService(
        IZipArchiveDecoder decoder,
        IZipEntryExtractionService extractionService)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(extractionService);

        _decoder = decoder;
        _extractionService = extractionService;
    }

    public async Task<IReadOnlyList<ZipEntryExtractionResult>> ProcessAsync(
        ArtifactId archiveArtifactId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var entries =
            await _decoder.DecodeAsync(
                content,
                cancellationToken);

        var results =
            new List<ZipEntryExtractionResult>(
                entries.Count);

        foreach (var entry in entries)
        {
            results.Add(
                await _extractionService.ExtractAsync(
                    archiveArtifactId,
                    entry.EntryName,
                    entry.Content,
                    cancellationToken));
        }

        return results;
    }
}

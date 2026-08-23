using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipArchiveProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_PersistsDecodedEntries()
    {
        var extraction = new RecordingExtractionService();

        var service =
            new ZipArchiveProcessingService(
                new StubDecoder(),
                extraction);

        var results =
            await service.ProcessAsync(
                new ArtifactId("archive-001"),
                "zip"u8.ToArray());

        Assert.Equal(2, results.Count);
        Assert.Equal(2, extraction.Entries.Count);
        Assert.Equal("one.txt", extraction.Entries[0]);
        Assert.Equal("two.txt", extraction.Entries[1]);
    }

    private sealed class StubDecoder :
        IZipArchiveDecoder
    {
        public Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DecodedArchiveEntry>>(
                [
                    new()
                    {
                        EntryName = "one.txt",
                        Content = "one"u8.ToArray()
                    },
                    new()
                    {
                        EntryName = "two.txt",
                        Content = "two"u8.ToArray()
                    }
                ]);
    }

    private sealed class RecordingExtractionService :
        IZipEntryExtractionService
    {
        public List<string> Entries { get; } = [];

        public Task<ZipEntryExtractionResult> ExtractAsync(
            ArtifactId archiveArtifactId,
            string entryName,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entryName);

            return Task.FromResult(
                new ZipEntryExtractionResult
                {
                    Artifact = new()
                    {
                        Id = new ArtifactId(entryName),
                        Name = entryName,
                        ArtifactType = "zip-entry"
                    },
                    Provenance = new()
                    {
                        ArtifactId = new ArtifactId(entryName),
                        Source = entryName,
                        RecordedBy = "test"
                    },
                    Relationships = []
                });
        }
    }
}

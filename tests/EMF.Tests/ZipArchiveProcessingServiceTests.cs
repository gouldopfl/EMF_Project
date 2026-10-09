using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipArchiveProcessingServiceTests
{
    [Fact]
    public async Task StreamProcessingPassesExactBorrowedStreamToDecoder()
    {
        var decoder = new StreamDecoder();
        var extraction = new RecordingExtractionService();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);
        var result = await new ZipArchiveProcessingService(decoder, extraction).ProcessAsync(new("parent"), stream);
        Assert.Same(stream, decoder.Observed);
        Assert.Single(result); Assert.Equal("entry.txt", extraction.Entries.Single());
        Assert.True(stream.CanRead);
    }
    [Fact]
    public async Task StreamProcessingDoesNotFallBackToRomOnlyDecoder()
    {
        using var stream = new MemoryStream(new byte[3]);
        await Assert.ThrowsAsync<NotSupportedException>(() => new ZipArchiveProcessingService(new StubDecoder(),
            new RecordingExtractionService()).ProcessAsync(new("parent"), stream));
    }
    private sealed class StreamDecoder : IZipArchiveDecoder
    {
        public Stream? Observed;
        public Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(ReadOnlyMemory<byte> content, CancellationToken ct = default) =>
            throw new Exception("Parent copying fallback forbidden");
        public Task<IReadOnlyList<DecodedArchiveEntry>> DecodeAsync(Stream content, CancellationToken ct = default)
        {
            Observed = content;
            return Task.FromResult<IReadOnlyList<DecodedArchiveEntry>>(new[] { new DecodedArchiveEntry { EntryName = "entry.txt", Content = new byte[] { 1 } } });
        }
    }

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

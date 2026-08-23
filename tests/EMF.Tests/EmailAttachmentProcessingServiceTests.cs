using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class EmailAttachmentProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_ProcessesDecodedAttachments()
    {
        var decoder = new StubDecoder();
        var extractor = new StubExtractor();

        var service =
            new EmailAttachmentProcessingService(
                decoder,
                extractor);

        var results =
            await service.ProcessAsync(
                new ArtifactId("email-001"),
                "eml"u8.ToArray());

        Assert.Equal(2, results.Count);
        Assert.Equal(2, extractor.Calls);
    }

    private sealed class StubDecoder : IEmailAttachmentDecoder
    {
        public Task<IReadOnlyList<DecodedEmailAttachment>> DecodeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DecodedEmailAttachment>>(
            [
                new()
                {
                    FileName = "a.txt",
                    Content = "a"u8.ToArray()
                },
                new()
                {
                    FileName = "b.txt",
                    Content = "b"u8.ToArray()
                }
            ]);
    }

    private sealed class StubExtractor :
        IEmailAttachmentExtractionService
    {
        public int Calls { get; private set; }

        public Task<EmailAttachmentExtractionResult> ExtractAsync(
            ArtifactId emailArtifactId,
            string fileName,
            string? contentType,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(
                new EmailAttachmentExtractionResult
                {
                    Artifact = new Artifact
                    {
                        Id = new ArtifactId(fileName),
                        Name = fileName,
                        ArtifactType = "email-attachment"
                    },
                    Provenance = new Provenance
                    {
                        ArtifactId = new ArtifactId(fileName),
                        Source = fileName,
                        RecordedBy = "test"
                    },
                    Relationships = []
                });
        }
    }
}

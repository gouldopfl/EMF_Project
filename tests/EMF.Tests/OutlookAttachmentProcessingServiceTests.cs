using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class OutlookAttachmentProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_PersistsDecodedAttachments()
    {
        var extraction = new RecordingExtractionService();

        var service =
            new OutlookAttachmentProcessingService(
                new StubDecoder(),
                extraction);

        var results =
            await service.ProcessAsync(
                new ArtifactId("msg-001"),
                "msg"u8.ToArray());

        Assert.Equal(2, results.Count);
        Assert.Equal(
            new[] { "one.txt", "two.pdf" },
            extraction.FileNames);
    }

    private sealed class StubDecoder :
        IOutlookMessageDecoder
    {
        public Task<DecodedOutlookMessage> DecodeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new DecodedOutlookMessage
                {
                    Attachments =
                    [
                        new()
                        {
                            FileName = "one.txt",
                            Content = "one"u8.ToArray()
                        },
                        new()
                        {
                            FileName = "two.pdf",
                            Content = "two"u8.ToArray()
                        }
                    ]
                });
    }

    private sealed class RecordingExtractionService :
        IEmailAttachmentExtractionService
    {
        public List<string> FileNames { get; } = [];

        public Task<EmailAttachmentExtractionResult> ExtractAsync(
            ArtifactId emailArtifactId,
            string fileName,
            string? contentType,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            FileNames.Add(fileName);

            var artifact =
                new Artifact
                {
                    Id = new ArtifactId(fileName),
                    Name = fileName,
                    ArtifactType = "email-attachment"
                };

            return Task.FromResult(
                new EmailAttachmentExtractionResult
                {
                    Artifact = artifact,
                    Provenance = new Provenance
                    {
                        ArtifactId = artifact.Id,
                        Source = fileName,
                        RecordedBy = "test"
                    },
                    Relationships = []
                });
        }
    }
}

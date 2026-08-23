using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EmailAttachmentWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesPersistedEmail()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var email = new Artifact
        {
            Id = new ArtifactId("email-001"),
            Name = "message.eml",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".eml"
            }
        };

        await repository.AddArtifactAsync(email);

        var store = new StubContentStore(
            email.Id,
            "eml"u8.ToArray());

        var processor = new StubProcessingService();

        var activity =
            new EmailAttachmentWorkflowActivity(
                repository,
                store,
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-email")
                });

        Assert.True(result.Succeeded);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenEmailContentMissing()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var email = new Artifact
        {
            Id = new ArtifactId("email-missing"),
            Name = "missing.eml",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".eml"
            }
        };

        await repository.AddArtifactAsync(email);

        var processor = new StubProcessingService();

        var activity =
            new EmailAttachmentWorkflowActivity(
                repository,
                new StubContentStore(),
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-email")
                });

        Assert.False(result.Succeeded);
        Assert.Equal(0, processor.Calls);
    }

    private sealed class StubProcessingService :
        IEmailAttachmentProcessingService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<EmailAttachmentExtractionResult>>
            ProcessAsync(
                ArtifactId emailArtifactId,
                ReadOnlyMemory<byte> content,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult<
                IReadOnlyList<EmailAttachmentExtractionResult>>(
                    Array.Empty<EmailAttachmentExtractionResult>());
        }
    }

    private sealed class StubContentStore :
        IArtifactContentStore
    {
        private readonly ArtifactId? _artifactId;
        private readonly byte[]? _content;

        public StubContentStore(
            ArtifactId? artifactId = null,
            byte[]? content = null)
        {
            _artifactId = artifactId;
            _content = content;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                artifactId == _artifactId
                    ? _content
                    : null);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

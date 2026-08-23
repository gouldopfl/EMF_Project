using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class OutlookAttachmentWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_ProcessesPersistedOutlookMessage()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var message = new Artifact
        {
            Id = new ArtifactId("outlook-001"),
            Name = "message.msg",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".msg"
            }
        };

        await repository.AddArtifactAsync(message);

        var store = new StubContentStore(
            message.Id,
            "eml"u8.ToArray());

        var processor = new StubProcessingService();

        var activity =
            new OutlookAttachmentWorkflowActivity(
                repository,
                store,
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-outlook")
                });

        Assert.True(result.Succeeded);
        Assert.Equal(1, processor.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenOutlookContentMissing()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var message = new Artifact
        {
            Id = new ArtifactId("outlook-missing"),
            Name = "missing.msg",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = ".msg"
            }
        };

        await repository.AddArtifactAsync(message);

        var processor = new StubProcessingService();

        var activity =
            new OutlookAttachmentWorkflowActivity(
                repository,
                new StubContentStore(),
                processor);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-outlook")
                });

        Assert.False(result.Succeeded);
        Assert.Equal(0, processor.Calls);
    }

    private sealed class StubProcessingService :
        IOutlookAttachmentProcessingService
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<EmailAttachmentExtractionResult>>
            ProcessAsync(
                ArtifactId messageArtifactId,
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

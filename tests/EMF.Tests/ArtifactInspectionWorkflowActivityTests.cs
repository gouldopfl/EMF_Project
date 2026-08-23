using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactInspectionWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_MergesInspectionMetadata()
    {
        var repository =
            new InMemoryEvidenceRepository();

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "evidence.xlsx",
                ArtifactType = "file",
                Fingerprint = new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = "abc"
                },
                Metadata = new Dictionary<string, object>
                {
                    [ArtifactMetadataKeys.SourcePath] =
                        "/tmp/evidence.xlsx"
                }
            };

        await repository.AddArtifactWithProvenanceAsync(
            artifact,
            new Provenance
            {
                ArtifactId = artifact.Id,
                Source = "/tmp/evidence.xlsx",
                RecordedBy = "test"
            });

        var activity =
            new ArtifactInspectionWorkflowActivity(
                new StubDiscovery(),
                new StubInspection(),
                repository,
                new StubFingerprintService(),
                "/tmp",
                new DiscoveryOptions());

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-001")
                });

        Assert.True(result.Succeeded);

        var updated =
            await repository.GetArtifactAsync(
                artifact.Id);

        Assert.NotNull(updated);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            updated.Metadata[ArtifactMetadataKeys.ContentType].ToString());
        Assert.Equal(
            "XLSX",
            updated.Metadata["detectedFormat"].ToString());
    }

    private sealed class StubDiscovery :
        IStreamingDiscoveryService
    {
        public async IAsyncEnumerable<DiscoveredItem>
            DiscoverItemsAsync(
                string sourcePath,
                DiscoveryOptions options,
                [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            yield return new DiscoveredItem
            {
                Name = "evidence.xlsx",
                SourcePath = "/tmp/evidence.xlsx",
                SourceType = "file"
            };

            await Task.CompletedTask;
        }
    }

    private sealed class StubInspection :
        IArtifactInspectionService
    {
        public Task<ArtifactInspectionResult> InspectAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ArtifactInspectionResult
                {
                    SourcePath = sourcePath,
                    DetectedContentType =
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    DetectedFormat = "XLSX"
                });
    }

    private sealed class StubFingerprintService :
        IContentFingerprintService
    {
        public Task<ContentFingerprint> ComputeAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = "abc"
                });

        public Task<ContentFingerprint> ComputeAsync(
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

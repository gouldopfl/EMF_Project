using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Integrity;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ContainerProcessingGuardTests
{
    [Fact]
    public async Task EvaluateAsync_AllowsUnprocessedMatchingContent()
    {
        var repository = new InMemoryEvidenceRepository();
        var fingerprints = new Sha256ContentFingerprintService();
        var content = "container"u8.ToArray();
        var fingerprint = await fingerprints.ComputeAsync(content);

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("container-001"),
                Name = "archive.zip",
                ArtifactType = "file",
                Fingerprint = fingerprint
            };

        await repository.AddArtifactAsync(artifact);

        var guard =
            new ContainerProcessingGuard(
                repository,
                fingerprints);

        var decision =
            await guard.EvaluateAsync(
                artifact,
                content,
                "zip",
                "1");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(fingerprint, decision.Fingerprint);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsSuccessfullyProcessedContent()
    {
        var repository = new InMemoryEvidenceRepository();
        var fingerprints = new Sha256ContentFingerprintService();
        var content = "container"u8.ToArray();
        var fingerprint = await fingerprints.ComputeAsync(content);

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("container-002"),
                Name = "archive.zip",
                ArtifactType = "file",
                Fingerprint = fingerprint
            };

        await repository.AddArtifactAsync(artifact);

        var guard =
            new ContainerProcessingGuard(
                repository,
                fingerprints);

        await guard.MarkProcessedAsync(
            artifact,
            fingerprint,
            "zip",
            "1");

        var persisted =
            await repository.GetArtifactAsync(artifact.Id);

        Assert.NotNull(persisted);

        var decision =
            await guard.EvaluateAsync(
                persisted,
                content,
                "zip",
                "1");

        Assert.False(decision.ShouldProcess);
    }

    [Fact]
    public async Task EvaluateAsync_ReprocessesWhenProcessorVersionChanges()
    {
        var repository = new InMemoryEvidenceRepository();
        var fingerprints = new Sha256ContentFingerprintService();
        var content = "container"u8.ToArray();
        var fingerprint = await fingerprints.ComputeAsync(content);

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("container-003"),
                Name = "archive.zip",
                ArtifactType = "file",
                Fingerprint = fingerprint
            };

        await repository.AddArtifactAsync(artifact);

        var guard =
            new ContainerProcessingGuard(
                repository,
                fingerprints);

        await guard.MarkProcessedAsync(
            artifact,
            fingerprint,
            "zip",
            "1");

        var persisted =
            await repository.GetArtifactAsync(artifact.Id);

        Assert.NotNull(persisted);

        var decision =
            await guard.EvaluateAsync(
                persisted,
                content,
                "zip",
                "2");

        Assert.True(decision.ShouldProcess);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsContentFingerprintMismatch()
    {
        var repository = new InMemoryEvidenceRepository();
        var fingerprints = new Sha256ContentFingerprintService();

        var expected =
            await fingerprints.ComputeAsync(
                "expected"u8.ToArray());

        var artifact =
            new Artifact
            {
                Id = new ArtifactId("container-004"),
                Name = "archive.zip",
                ArtifactType = "file",
                Fingerprint = expected
            };

        await repository.AddArtifactAsync(artifact);

        var guard =
            new ContainerProcessingGuard(
                repository,
                fingerprints);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => guard.EvaluateAsync(
                    artifact,
                    "changed"u8.ToArray(),
                    "zip",
                    "1"));

        Assert.Equal(
            "Container content fingerprint does not match the artifact fingerprint.",
            ex.Message);
    }
}

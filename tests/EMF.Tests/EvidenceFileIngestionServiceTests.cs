using EMF.Integrity;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceFileIngestionServiceTests
{
    [Fact]
    public async Task IngestAsync_PersistsFileThroughDurableOwnedCoordinator()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var service = new EvidenceFileIngestionService(f.Service(), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        var result = await service.IngestAsync(f.SourcePath);
        Assert.Equal(f.Id, result.Artifact.Id); Assert.Equal("file", result.Artifact.ArtifactType); Assert.False(result.AlreadyExisted);
        Assert.True(result.IsAdopted); Assert.Equal(ArtifactIngestionState.Completed, result.LifecycleState);
        Assert.NotNull(await f.Repository.GetArtifactAsync(f.Id)); Assert.NotNull(await f.Physical.ReadAsync(f.Id));
    }
    [Theory]
    [InlineData("artifact")]
    [InlineData("fingerprint")]
    [InlineData("provenance")]
    [InlineData("source")]
    public async Task IngestAsync_RejectsInvalidFactoryBindingsBeforePreparingLifecycle(string invalid)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var service = new EvidenceFileIngestionService(f.Service(), f.Fingerprints, new FixedId(f.Id), new InvalidFactory(invalid));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(f.SourcePath));
        Assert.Null(await f.Physical.ReadAsync(f.Id)); Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        await using var session = await f.Persistence.AcquireAsync(f.SecurityContext.Operation.OperationId); Assert.Null(session.Intent);
    }
    [Fact]
    public async Task IngestAsync_RejectsOversizedFileBeforePreparingLifecycle()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var service = new EvidenceFileIngestionService(f.Service(), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory(), maxFileBytes: 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.IngestAsync(f.SourcePath)); Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public async Task IngestAsync_AuditFailureReportsCommittedResultWithPendingDelivery()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var service = new EvidenceFileIngestionService(f.Service(audit: new ArtifactIngestionFixture.AuditOutage()), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        var result = await service.IngestAsync(f.SourcePath);
        Assert.True(result.IsAdopted); Assert.Equal(IngestionAuditDelivery.Pending, result.AuditDelivery);
        Assert.Equal(ArtifactIngestionState.MetadataCommitted, result.LifecycleState);
    }
    [Fact]
    public async Task IngestAsync_CancellationAfterCreationLeavesDurableRecoverableOwnership()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        var store = new ArtifactIngestionFixture.FaultStore(f.Physical) { CancelAfterCreate = true };
        var service = new EvidenceFileIngestionService(f.Service(physical: store), f.Fingerprints, new FixedId(f.Id), new ArtifactFactory());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IngestAsync(f.SourcePath));
        var intent = await f.IntentAsync(); Assert.Equal(ArtifactIngestionState.Cleaned, intent.State);
        Assert.NotNull(await f.Physical.GetMutationOutcomeAsync(intent.OperationId));
        File.Delete(f.SourcePath); // recovery must not reopen the original source
        Assert.Equal(ArtifactIngestionState.Cleaned, (await f.Restart().RecoverOperationAsync(intent.OperationId)).State);
        Assert.Null(await f.Physical.ReadAsync(f.Id));
    }
    [Fact]
    public void LegacyConstructorFailsCapabilityAdmission()
    {
#pragma warning disable CS0618
        Assert.Throws<NotSupportedException>(() => new EvidenceFileIngestionService(null!, null!, new Sha256ContentFingerprintService(), new FixedId(new("synthetic")), new ArtifactFactory()));
#pragma warning restore CS0618
    }
    private sealed class FixedId(ArtifactId id) : IArtifactIdGenerator { public ArtifactId Generate() => id; }
    private sealed class InvalidFactory(string invalid) : IArtifactFactory
    {
        public ArtifactCreationResult Create(EMF.Discovery.Models.DiscoveredItem item, ArtifactId id, ContentFingerprint? fingerprint)
        {
            var valid = new ArtifactFactory().Create(item, id, fingerprint);
            return new() { Artifact = new Artifact { Id = invalid == "artifact" ? new("synthetic-wrong") : id,
                Name = valid.Artifact.Name, ArtifactType = valid.Artifact.ArtifactType,
                Fingerprint = invalid == "fingerprint" ? new() { Algorithm = "SHA256", Value = "synthetic-wrong" } : fingerprint },
                Provenance = new() { ArtifactId = invalid == "provenance" ? new("synthetic-wrong") : id,
                    Source = invalid == "source" ? "synthetic-wrong" : item.SourcePath, RecordedBy = "synthetic" } };
        }
    }
}

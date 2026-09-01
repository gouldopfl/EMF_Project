using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed partial class EvidencePackageServiceTests
{
    [Fact]
    public void Service_ImplementsEvidencePackageContract()
    {
        IEvidencePackageService service =
            new EvidencePackageService(
                new RecordingRepository(),
                new GuidIdGenerator());

        Assert.NotNull(service);
    }

    [Fact]
    public async Task CreateAsync_PersistsEvidencePackage()
    {
        var repository = new RecordingRepository();

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var claimIssueId =
            new ClaimIssueId("issue-1");

        var result =
            await service.CreateAsync(
                claimIssueId,
                "Medical review",
                "MedicalProfessional");

        Assert.Equal(
            claimIssueId,
            result.ClaimIssueId);

        Assert.Equal(
            "Medical review",
            result.Purpose);

        Assert.Equal(
            "MedicalProfessional",
            result.ReviewerRole);

        Assert.NotEmpty(result.Id.Value);
        Assert.Same(result, repository.Package);
    }

    private sealed class RecordingRepository :
        IEvidencePackageRepository
    {
        public EvidencePackage? Package { get; private set; }

        public EvidencePackageArtifact? Artifact { get; private set; }

        public Task AddEvidencePackageAsync(
            EvidencePackage evidencePackage,
            CancellationToken cancellationToken = default)
        {
            Package = evidencePackage;
            return Task.CompletedTask;
        }

        public Task AddEvidencePackageArtifactAsync(
            EvidencePackageArtifact artifact,
            CancellationToken cancellationToken = default)
        {
            Artifact = artifact;
            return Task.CompletedTask;
        }

        public Task<EvidencePackage?> GetEvidencePackageAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidencePackage?>(null);

        public Task<IReadOnlyList<EvidencePackage>>
            GetEvidencePackagesAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidencePackage>>(
                Array.Empty<EvidencePackage>());
    }
}

public sealed partial class EvidencePackageServiceTests
{
    [Fact]
    public async Task AddArtifactAsync_PersistsPackageArtifact()
    {
        var repository = new RecordingRepository();

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var packageId =
            new EvidencePackageId("package-1");

        var artifactId =
            new EMF.Core.Models.Identities.ArtifactId(
                "artifact-1");

        var result =
            await service.AddArtifactAsync(
                packageId,
                artifactId,
                EvidencePackageContentRoles.UnderlyingEvidence);

        Assert.Equal(
            packageId,
            result.EvidencePackageId);

        Assert.Equal(
            artifactId,
            result.ArtifactId);

        Assert.Equal(
            EvidencePackageContentRoles.UnderlyingEvidence,
            result.ContentRole);

        Assert.Same(
            result,
            repository.Artifact);
    }

    [Fact]
    public async Task AddArtifactAsync_RejectsUnsupportedContentRole()
    {
        var service =
            new EvidencePackageService(
                new RecordingRepository(),
                new GuidIdGenerator());

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                service.AddArtifactAsync(
                    new EvidencePackageId("package-1"),
                    new EMF.Core.Models.Identities.ArtifactId(
                        "artifact-1"),
                    "UnsupportedRole"));
    }
}

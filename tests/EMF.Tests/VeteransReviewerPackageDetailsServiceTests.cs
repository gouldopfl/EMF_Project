using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_MissingPackageReturnsNull()
    {
        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService(),
                new InMemoryEvidenceRepository());

        var result =
            await service.GetAsync(
                new EvidencePackageId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ResolvesPackageArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var artifact = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(artifact);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        artifact.Id)
                },
                evidence);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Equal(packageId, result.PackageDetails.Package.Id);
        Assert.Same(artifact, Assert.Single(result.Artifacts));
    }

    [Fact]
    public async Task GetAsync_SkipsMissingArtifacts()
    {
        var packageId = new EvidencePackageId("package-1");
        var existing = CreateArtifact("artifact-1");

        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(existing);

        var service =
            new VeteransReviewerPackageDetailsService(
                new RecordingPackageService
                {
                    Details = CreateDetails(
                        packageId,
                        existing.Id,
                        new ArtifactId("missing"))
                },
                evidence);

        var result = await service.GetAsync(packageId);

        Assert.NotNull(result);
        Assert.Same(existing, Assert.Single(result.Artifacts));
    }

    private static Artifact CreateArtifact(string id) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = id,
            ArtifactType = "test"
        };

    private static EvidencePackageDetails CreateDetails(
        EvidencePackageId packageId,
        params ArtifactId[] artifactIds) =>
        new()
        {
            Package = new EvidencePackage
            {
                Id = packageId,
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Physician reviewer package",
                ReviewerRole = "MedicalProfessional"
            },
            Artifacts =
                artifactIds
                    .Select(
                        id => new EvidencePackageArtifact
                        {
                            EvidencePackageId = packageId,
                            ArtifactId = id,
                            ContentRole =
                                EvidencePackageContentRoles
                                    .UnderlyingEvidence
                        })
                    .ToArray()
        };
}

file sealed class RecordingPackageService :
    IEvidencePackageService
{
    public EvidencePackageDetails? Details { get; init; }

    public Task<EvidencePackageDetails?> GetAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Details);

    public Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<EvidencePackageArtifact> AddArtifactAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string contentRole,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

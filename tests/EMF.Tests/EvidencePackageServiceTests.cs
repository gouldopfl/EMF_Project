using EMF.Common;
using EMF.Core.Models.Identities;
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

    [Fact]
    public async Task CreateAsync_PersistsInitialArtifactsAtomically()
    {
        var repository = new RecordingRepository();

        IEvidencePackageService service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var result =
            await service.CreateAsync(
                new ClaimIssueId("issue-atomic"),
                "Medical review",
                "MedicalProfessional",
                [new ArtifactId("artifact-evidence")],
                [new ArtifactId("artifact-summary")]);

        Assert.Equal(2, repository.InitialArtifacts.Count);

        Assert.Contains(
            repository.InitialArtifacts,
            artifact =>
                artifact.EvidencePackageId == result.Id &&
                artifact.ArtifactId ==
                    new ArtifactId("artifact-evidence") &&
                artifact.ContentRole ==
                    EvidencePackageContentRoles.UnderlyingEvidence);

        Assert.Contains(
            repository.InitialArtifacts,
            artifact =>
                artifact.EvidencePackageId == result.Id &&
                artifact.ArtifactId ==
                    new ArtifactId("artifact-summary") &&
                artifact.ContentRole ==
                    EvidencePackageContentRoles
                        .GeneratedOrganizationalMaterial);
    }

    [Fact]
    public async Task CreateAsync_RejectsConflictingArtifactRoles()
    {
        var repository = new RecordingRepository();

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var artifactId =
            new ArtifactId("artifact-1");

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        new ClaimIssueId("issue-1"),
                        "Medical review",
                        "MedicalProfessional",
                        [artifactId],
                        [artifactId]));

        Assert.Contains(
            artifactId.Value,
            exception.Message);

        Assert.Null(repository.Package);
        Assert.Empty(repository.InitialArtifacts);
    }

    private sealed class RecordingRepository :
        IEvidencePackageRepository
    {
        public EvidencePackage? Package { get; private set; }

        public EvidencePackageArtifact? Artifact { get; private set; }

        public IReadOnlyList<EvidencePackageArtifact>
            InitialArtifacts { get; private set; } = [];

        public EvidencePackage? ExistingPackage { get; set; }

        public IReadOnlyList<EvidencePackage> ExistingPackages { get; set; } = [];

        public IReadOnlyList<EvidencePackageArtifact>
            ExistingArtifacts { get; set; } = [];

        public int ArtifactQueryCount { get; private set; }

        public Task AddEvidencePackageAsync(
            EvidencePackage evidencePackage,
            CancellationToken cancellationToken = default)
        {
            Package = evidencePackage;
            return Task.CompletedTask;
        }

        public Task AddEvidencePackageAsync(
            EvidencePackage evidencePackage,
            IReadOnlyCollection<EvidencePackageArtifact> artifacts,
            CancellationToken cancellationToken = default)
        {
            Package = evidencePackage;
            InitialArtifacts = artifacts.ToArray();
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
            Task.FromResult(ExistingPackage);

        public Task<IReadOnlyList<EvidencePackageArtifact>>
            GetEvidencePackageArtifactsAsync(
                EvidencePackageId evidencePackageId,
                CancellationToken cancellationToken = default)
        {
            ArtifactQueryCount++;

            return Task.FromResult(
                ExistingArtifacts);
        }

        public Task<IReadOnlyList<EvidencePackage>>
            GetEvidencePackagesAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ExistingPackages);
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

    [Fact]
    public async Task AddArtifactAsync_RejectsConflictingContentRole()
    {
        var packageId =
            new EvidencePackageId("package-1");

        var artifactId =
            new ArtifactId("artifact-1");

        var repository =
            new RecordingRepository
            {
                ExistingArtifacts =
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = packageId,
                        ArtifactId = artifactId,
                        ContentRole =
                            EvidencePackageContentRoles
                                .UnderlyingEvidence
                    }
                ]
            };

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.AddArtifactAsync(
                        packageId,
                        artifactId,
                        EvidencePackageContentRoles
                            .GeneratedOrganizationalMaterial));

        Assert.Contains(
            packageId.Value,
            exception.Message);

        Assert.Contains(
            artifactId.Value,
            exception.Message);

        Assert.Null(repository.Artifact);
    }
}

public sealed partial class EvidencePackageServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsPackageDetails()
    {
        var package =
            new EvidencePackage
            {
                Id = new EvidencePackageId("package-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            };

        var artifact =
            new EvidencePackageArtifact
            {
                EvidencePackageId = package.Id,
                ArtifactId =
                    new EMF.Core.Models.Identities.ArtifactId(
                        "artifact-1"),
                ContentRole =
                    EvidencePackageContentRoles.UnderlyingEvidence
            };

        var repository =
            new RecordingRepository
            {
                ExistingPackage = package,
                ExistingArtifacts = [artifact]
            };

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var result =
            await service.GetAsync(
                package.Id);

        Assert.NotNull(result);
        Assert.Same(
            package,
            result!.Package);

        Assert.Same(
            artifact,
            Assert.Single(result.Artifacts));
    }

    [Fact]
    public async Task GetAsync_RejectsUnsupportedPersistedContentRole()
    {
        var package =
            new EvidencePackage
            {
                Id = new EvidencePackageId("package-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            };

        var repository =
            new RecordingRepository
            {
                ExistingPackage = package,
                ExistingArtifacts =
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = package.Id,
                        ArtifactId = new ArtifactId("artifact-1"),
                        ContentRole = "UnsupportedRole"
                    }
                ]
            };

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(package.Id));

        Assert.Contains(
            package.Id.Value,
            exception.Message);

        Assert.Contains(
            "UnsupportedRole",
            exception.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsArtifactFromDifferentPackage()
    {
        var package =
            new EvidencePackage
            {
                Id = new EvidencePackageId("package-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            };

        var otherPackageId =
            new EvidencePackageId("package-2");

        var repository =
            new RecordingRepository
            {
                ExistingPackage = package,
                ExistingArtifacts =
                [
                    new EvidencePackageArtifact
                    {
                        EvidencePackageId = otherPackageId,
                        ArtifactId = new ArtifactId("artifact-1"),
                        ContentRole =
                            EvidencePackageContentRoles
                                .UnderlyingEvidence
                    }
                ]
            };

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(package.Id));

        Assert.Contains(
            package.Id.Value,
            exception.Message);

        Assert.Contains(
            otherPackageId.Value,
            exception.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsDifferentReturnedPackage()
    {
        var requestedPackageId =
            new EvidencePackageId("package-1");

        var returnedPackageId =
            new EvidencePackageId("package-2");

        var repository =
            new RecordingRepository
            {
                ExistingPackage =
                    new EvidencePackage
                    {
                        Id = returnedPackageId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-1"),
                        Purpose = "Medical review",
                        ReviewerRole = "MedicalProfessional"
                    }
            };

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(requestedPackageId));

        Assert.Contains(
            requestedPackageId.Value,
            exception.Message);

        Assert.Contains(
            returnedPackageId.Value,
            exception.Message);

        Assert.Equal(
            0,
            repository.ArtifactQueryCount);
    }
}

public sealed partial class EvidencePackageServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenPackageDoesNotExist()
    {
        var repository =
            new RecordingRepository();

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var result =
            await service.GetAsync(
                new EvidencePackageId("missing-package"));

        Assert.Null(result);
        Assert.Equal(
            0,
            repository.ArtifactQueryCount);
    }
}

public sealed partial class EvidencePackageServiceTests
{
    [Fact]
    public async Task GetAsync_ByClaimIssue_ReturnsPackageDetails()
    {
        var repository =
            new RecordingRepository
            {
                ExistingPackages =
                [
                    new EvidencePackage
                    {
                        Id = new EvidencePackageId("package-1"),
                        ClaimIssueId = new ClaimIssueId("issue-1"),
                        Purpose = "Medical review",
                        ReviewerRole = "MedicalProfessional"
                    }
                ]
            };

        repository.ExistingArtifacts =
            [
                new EvidencePackageArtifact
                {
                    EvidencePackageId =
                        repository.ExistingPackages[0].Id,
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            "artifact-1"),
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence
                }
            ];

        var service =
            new EvidencePackageService(
                repository,
                new GuidIdGenerator());

        var result =
            await service.GetAsync(
                new ClaimIssueId("issue-1"));

        var details =
            Assert.Single(result);

        Assert.Same(
            repository.ExistingPackages[0],
            details.Package);

        var artifact =
            Assert.Single(
                details.Artifacts);

        Assert.Equal(
            "artifact-1",
            artifact.ArtifactId.Value);
    }
}

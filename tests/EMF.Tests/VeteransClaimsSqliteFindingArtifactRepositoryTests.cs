using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteFindingArtifactRepositoryTests
{
    [Fact]
    public async Task FindingArtifact_RoundTripsInBothDirections()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteFindingRepository(path);
            await repository.InitializeAsync();

            var finding = await AddFindingAsync(path, repository);

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-finding"),
                Name = "Finding evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            var association = new FindingArtifact
            {
                FindingId = finding.Id,
                ArtifactId = artifact.Id,
                Role = FindingTraceabilityRoles.Supporting
            };

            await repository.AddFindingArtifactAsync(association);

            var byFinding = Assert.Single(
                await repository.GetFindingArtifactsAsync(finding.Id));
            var byArtifact = Assert.Single(
                await repository.GetFindingArtifactsAsync(artifact.Id));

            Assert.Equal(finding.Id, byFinding.FindingId);
            Assert.Equal(artifact.Id, byFinding.ArtifactId);
            Assert.Equal(
                FindingTraceabilityRoles.Supporting,
                byFinding.Role);
            Assert.Equal(byFinding.FindingId, byArtifact.FindingId);
            Assert.Equal(byFinding.ArtifactId, byArtifact.ArtifactId);
            Assert.Equal(byFinding.Role, byArtifact.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FindingArtifact_RejectsInvalidRole()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteFindingRepository(path);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddFindingArtifactAsync(
                    new FindingArtifact
                    {
                        FindingId = new FindingId("finding"),
                        ArtifactId = new ArtifactId("artifact"),
                        Role = "Unknown"
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FindingArtifact_RejectsMissingArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteFindingRepository(path);
            await repository.InitializeAsync();

            var finding = await AddFindingAsync(path, repository);

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddFindingArtifactAsync(
                    new FindingArtifact
                    {
                        FindingId = finding.Id,
                        ArtifactId =
                            new ArtifactId("missing-artifact"),
                        Role = FindingTraceabilityRoles.Qualifying
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FindingArtifact_RejectsMissingFinding()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository = new SqliteFindingRepository(path);
            await repository.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-without-finding"),
                Name = "Unlinked finding evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddFindingArtifactAsync(
                    new FindingArtifact
                    {
                        FindingId = new FindingId("missing-finding"),
                        ArtifactId = artifact.Id,
                        Role = FindingTraceabilityRoles.Contradicting
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<Finding> AddFindingAsync(
        string path,
        SqliteFindingRepository repository)
    {
        var veteran = new Veteran
        {
            Id = new VeteranId("v-finding-artifact")
        };
        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-finding-artifact"),
            VeteranId = veteran.Id
        };
        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        var claimIssue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-finding-artifact"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };
        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(claimIssue);

        var finding = new Finding
        {
            Id = new FindingId("finding-artifact"),
            ClaimIssueId = claimIssue.Id,
            RequirementId = null,
            Outcome = FindingOutcomes.Favorable,
            Description = "Finding supported by artifact."
        };
        await repository.AddFindingAsync(finding);

        return finding;
    }
}

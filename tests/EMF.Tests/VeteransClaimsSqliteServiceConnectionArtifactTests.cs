using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteServiceConnectionArtifactTests
{
    [Fact]
    public async Task BasisArtifact_RoundTripsRole()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran { Id = new VeteranId("v-art-1") };
            await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("c-art-1"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path).AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("i-art-1"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };
            await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

            var repo = new SqliteServiceConnectionRepository(path);
            var theory = new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("t-art-1"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };
            await repo.AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("b-art-1"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };
            await repo.AddServiceConnectionBasisAsync(basis);

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-1"),
                Name = "Evidence",
                ArtifactType = "Test"
            };
            await evidence.AddArtifactAsync(artifact);

            await repo.AddBasisArtifactAsync(
                new ServiceConnectionBasisArtifact
                {
                    ServiceConnectionBasisId = basis.Id,
                    ArtifactId = artifact.Id,
                    Role = ServiceConnectionBasisTraceabilityRoles.Supporting
                });

            var stored = Assert.Single(
                await repo.GetBasisArtifactsAsync(basis.Id));

            Assert.Equal(artifact.Id, stored.ArtifactId);
            Assert.Equal(
                ServiceConnectionBasisTraceabilityRoles.Supporting,
                stored.Role);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BasisArtifact_RejectsInvalidRole()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repo = new SqliteServiceConnectionRepository(path);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repo.AddBasisArtifactAsync(
                    new ServiceConnectionBasisArtifact
                    {
                        ServiceConnectionBasisId =
                            new ServiceConnectionBasisId("basis-1"),
                        ArtifactId = new ArtifactId("artifact-1"),
                        Role = "Unknown"
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task BasisArtifact_RejectsMissingArtifact()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var veteran = new Veteran { Id = new VeteranId("v-art-missing") };
            await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("c-art-missing"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path).AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("i-art-missing"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };
            await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

            var repo = new SqliteServiceConnectionRepository(path);

            var theory = new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("t-art-missing"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };
            await repo.AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("b-art-missing"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };
            await repo.AddServiceConnectionBasisAsync(basis);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.AddBasisArtifactAsync(
                    new ServiceConnectionBasisArtifact
                    {
                        ServiceConnectionBasisId = basis.Id,
                        ArtifactId = new ArtifactId("missing-artifact"),
                        Role = ServiceConnectionBasisTraceabilityRoles.Supporting
                    }));
        }
        finally
        {
            File.Delete(path);
        }
    }

}

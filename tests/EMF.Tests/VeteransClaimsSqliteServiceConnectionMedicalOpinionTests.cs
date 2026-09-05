using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteServiceConnectionMedicalOpinionTests
{
    private static async Task<(
        SqliteServiceConnectionRepository Repository,
        ServiceConnectionBasis Basis)>
        CreateBasisAsync(string databasePath)
    {
        await new VeteransClaimsSqliteSchema(databasePath)
            .InitializeAsync();

        var veteran = new Veteran
        {
            Id = new VeteranId("veteran-medop-001")
        };

        await new SqliteVeteranRepository(databasePath)
            .AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-medop-001"),
            VeteranId = veteran.Id
        };

        await new SqliteClaimRepository(databasePath)
            .AddClaimAsync(claim);

        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-medop-001"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        await new SqliteClaimIssueRepository(databasePath)
            .AddClaimIssueAsync(issue);

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-medop-001"),
            ClaimIssueId = issue.Id,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var repository =
            new SqliteServiceConnectionRepository(databasePath);

        await repository.AddServiceConnectionTheoryAsync(theory);

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-medop-001"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory.Id
        };

        await repository.AddServiceConnectionBasisAsync(basis);

        return (repository, basis);
    }

    [Fact]
    public async Task BasisMedicalOpinion_RoundTripsRole()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var (repository, basis) =
                await CreateBasisAsync(databasePath);

            var opinion = new MedicalOpinion
            {
                Id = new MedicalOpinionId("opinion-medop-001"),
                ClaimIssueId = basis.ClaimIssueId,
                Question = "Is the condition related to service?",
                Opinion = "At least as likely as not."
            };

            await new SqliteMedicalOpinionRepository(databasePath)
                .AddMedicalOpinionAsync(opinion);

            await repository.AddBasisMedicalOpinionAsync(
                new ServiceConnectionBasisMedicalOpinion
                {
                    ServiceConnectionBasisId = basis.Id,
                    MedicalOpinionId = opinion.Id,
                    Role =
                        ServiceConnectionBasisTraceabilityRoles.Supporting
                });

            var stored =
                Assert.Single(
                    await repository.GetBasisMedicalOpinionsAsync(
                        basis.Id));

            Assert.Equal(basis.Id, stored.ServiceConnectionBasisId);
            Assert.Equal(opinion.Id, stored.MedicalOpinionId);
            Assert.Equal(
                ServiceConnectionBasisTraceabilityRoles.Supporting,
                stored.Role);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task BasisMedicalOpinion_RejectsDifferentClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var (repository, basis) =
                await CreateBasisAsync(databasePath);

            var otherIssue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-medop-other"),
                ClaimId = new ClaimId("claim-medop-001"),
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(otherIssue);

            var opinion = new MedicalOpinion
            {
                Id = new MedicalOpinionId("opinion-medop-other"),
                ClaimIssueId = otherIssue.Id,
                Question = "Other issue?",
                Opinion = "No."
            };

            await new SqliteMedicalOpinionRepository(databasePath)
                .AddMedicalOpinionAsync(opinion);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddBasisMedicalOpinionAsync(
                    new ServiceConnectionBasisMedicalOpinion
                    {
                        ServiceConnectionBasisId = basis.Id,
                        MedicalOpinionId = opinion.Id,
                        Role =
                            ServiceConnectionBasisTraceabilityRoles
                                .Contradicting
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task BasisMedicalOpinion_RejectsInvalidRole()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteServiceConnectionRepository(databasePath);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddBasisMedicalOpinionAsync(
                    new ServiceConnectionBasisMedicalOpinion
                    {
                        ServiceConnectionBasisId =
                            new ServiceConnectionBasisId("basis-1"),
                        MedicalOpinionId =
                            new MedicalOpinionId("opinion-1"),
                        Role = "Unknown"
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

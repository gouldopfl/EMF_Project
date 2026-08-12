using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteServiceConnectionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsTheoriesAndBases()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(
                databasePath)
                .AddClaimIssueAsync(claimIssue);

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId = claimIssue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            await repository
                .AddServiceConnectionTheoryAsync(
                    theory);

            var stored =
                await repository
                    .GetServiceConnectionTheoryAsync(
                        theory.Id);

            var issueTheories =
                await repository
                    .GetServiceConnectionTheoriesAsync(
                        claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(theory.Id, stored!.Id);
            Assert.Equal(
                theory.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                theory.TheoryType,
                stored.TheoryType);

            Assert.Equal(
                theory.Id,
                Assert.Single(issueTheories).Id);

            var basis = new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-001"),
                ClaimIssueId = claimIssue.Id,
                ServiceConnectionTheoryId =
                    theory.Id
            };

            await repository
                .AddServiceConnectionBasisAsync(
                    basis);

            var storedBasis =
                await repository
                    .GetServiceConnectionBasisAsync(
                        basis.Id);

            var issueBases =
                await repository
                    .GetServiceConnectionBasesAsync(
                        claimIssue.Id);

            var theoryBases =
                await repository
                    .GetServiceConnectionBasesAsync(
                        theory.Id);

            Assert.NotNull(storedBasis);
            Assert.Equal(
                basis.Id,
                storedBasis!.Id);
            Assert.Equal(
                basis.ClaimIssueId,
                storedBasis.ClaimIssueId);
            Assert.Equal(
                basis.ServiceConnectionTheoryId,
                storedBasis.ServiceConnectionTheoryId);

            Assert.Equal(
                basis.Id,
                Assert.Single(issueBases).Id);

            Assert.Equal(
                basis.Id,
                Assert.Single(theoryBases).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task
        Repository_RejectsTheoryForMissingClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            await repository.InitializeAsync();

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId =
                    new ClaimIssueId(
                        "missing-claim-issue"),
                TheoryType =
                    ServiceConnectionTheoryTypes.Direct
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddServiceConnectionTheoryAsync(
                        theory));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task
        Repository_RejectsBasisForDifferentClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var firstIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var secondIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var issueRepository =
                new SqliteClaimIssueRepository(
                    databasePath);

            await issueRepository.AddClaimIssueAsync(
                firstIssue);

            await issueRepository.AddClaimIssueAsync(
                secondIssue);

            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId = firstIssue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Direct
            };

            await repository
                .AddServiceConnectionTheoryAsync(
                    theory);

            var basis = new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-001"),
                ClaimIssueId = secondIssue.Id,
                ServiceConnectionTheoryId =
                    theory.Id
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddServiceConnectionBasisAsync(
                        basis));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}

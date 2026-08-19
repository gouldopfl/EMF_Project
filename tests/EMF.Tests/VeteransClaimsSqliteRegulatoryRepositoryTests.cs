using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteRegulatoryRepositoryTests
{
    [Fact]
    public async Task
        Repository_RoundTripsRegulatoryHierarchy()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteRegulatoryRepository(
                    databasePath);

            await repository.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id =
                    new RegulatoryAuthorityId(
                        "authority-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title =
                    "Pensions, Bonuses, and Veterans Relief"
            };

            await repository
                .AddRegulatoryAuthorityAsync(authority);

            var storedAuthority =
                await repository
                    .GetRegulatoryAuthorityAsync(
                        authority.Id);

            var authorities =
                await repository
                    .GetRegulatoryAuthoritiesAsync();

            Assert.NotNull(storedAuthority);
            Assert.Equal(
                authority.Id,
                storedAuthority!.Id);
            Assert.Equal(
                authority.AuthorityType,
                storedAuthority.AuthorityType);
            Assert.Equal(
                authority.Citation,
                storedAuthority.Citation);
            Assert.Equal(
                authority.Title,
                storedAuthority.Title);

            Assert.Equal(
                authority.Id,
                Assert.Single(authorities).Id);

            var provision = new RegulatoryProvision
            {
                Id =
                    new RegulatoryProvisionId(
                        "provision-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType =
                    RegulatoryProvisionTypes.Presumption,
                Citation = "38 CFR 3.309"
            };

            await repository
                .AddRegulatoryProvisionAsync(provision);

            var storedProvision =
                await repository
                    .GetRegulatoryProvisionAsync(
                        provision.Id);

            var provisions =
                await repository
                    .GetRegulatoryProvisionsAsync(
                        authority.Id);

            Assert.NotNull(storedProvision);
            Assert.Equal(
                provision.Id,
                storedProvision!.Id);
            Assert.Equal(
                provision.RegulatoryAuthorityId,
                storedProvision.RegulatoryAuthorityId);
            Assert.Equal(
                provision.ProvisionType,
                storedProvision.ProvisionType);
            Assert.Equal(
                provision.Citation,
                storedProvision.Citation);

            Assert.Equal(
                provision.Id,
                Assert.Single(provisions).Id);

            var requirement = new Requirement
            {
                Id =
                    new RequirementId(
                        "requirement-001"),
                RegulatoryProvisionId = provision.Id,
                Description =
                    "Applicable adjudicative requirement"
            };

            await repository
                .AddRequirementAsync(requirement);

            var storedRequirement =
                await repository
                    .GetRequirementAsync(
                        requirement.Id);

            var requirements =
                await repository
                    .GetRequirementsAsync(
                        provision.Id);

            Assert.NotNull(storedRequirement);
            Assert.Equal(
                requirement.Id,
                storedRequirement!.Id);
            Assert.Equal(
                requirement.RegulatoryProvisionId,
                storedRequirement.RegulatoryProvisionId);
            Assert.Equal(
                requirement.Description,
                storedRequirement.Description);

            Assert.Equal(
                requirement.Id,
                Assert.Single(requirements).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task
        Repository_RejectsOrphanedRegulatoryRecords()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteRegulatoryRepository(
                    databasePath);

            await repository.InitializeAsync();

            var orphanedProvision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId(
                            "orphaned-provision"),
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId(
                            "missing-authority"),
                    ProvisionType =
                        RegulatoryProvisionTypes.Requirement,
                    Citation = "38 CFR 3.303"
                };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddRegulatoryProvisionAsync(
                        orphanedProvision));

            var orphanedRequirement =
                new Requirement
                {
                    Id =
                        new RequirementId(
                            "orphaned-requirement"),
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId(
                            "missing-provision"),
                    Description =
                        "Orphaned adjudicative requirement"
                };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddRequirementAsync(
                        orphanedRequirement));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task
        Repository_AllowsMultipleVersionsOfSameCitation()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteRegulatoryRepository(databasePath);

            await repository.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-versioned"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Pensions, Bonuses, and Veterans Relief"
            };

            await repository.AddRegulatoryAuthorityAsync(authority);

            var first = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-3.310-2026"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310",
                Version = "2026-01",
                EffectiveFrom = new DateTimeOffset(
                    2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo = new DateTimeOffset(
                    2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
                SourceUri = "https://www.ecfr.gov/",
                SourceHash = "sha256:version-2026",
                RetrievedUtc = DateTimeOffset.UtcNow
            };

            var second = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-3.310-2027"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310",
                Version = "2027-01",
                EffectiveFrom = new DateTimeOffset(
                    2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                SourceUri = "https://www.ecfr.gov/",
                SourceHash = "sha256:version-2027",
                RetrievedUtc = DateTimeOffset.UtcNow
            };

            await repository.AddRegulatoryProvisionAsync(first);
            await repository.AddRegulatoryProvisionAsync(second);

            var provisions =
                await repository.GetRegulatoryProvisionsAsync(
                    authority.Id);

            Assert.Equal(2, provisions.Count);

            var storedFirst =
                provisions.Single(x => x.Id == first.Id);

            var storedSecond =
                provisions.Single(x => x.Id == second.Id);

            Assert.Equal("38 CFR 3.310", storedFirst.Citation);
            Assert.Equal("38 CFR 3.310", storedSecond.Citation);
            Assert.Equal("2026-01", storedFirst.Version);
            Assert.Equal("2027-01", storedSecond.Version);
            Assert.NotEqual(
                storedFirst.SourceHash,
                storedSecond.SourceHash);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}

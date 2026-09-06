using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteFindingRegulatoryProvisionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsFindingRegulatoryProvision()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteFindingRepository(databasePath);
            await repository.InitializeAsync();

            var (finding, provision) =
                await SeedFindingAndProvisionAsync(databasePath);

            var association = new FindingRegulatoryProvision
            {
                FindingId = finding.Id,
                RegulatoryProvisionId = provision.Id,
                Role = FindingTraceabilityRoles.Supporting
            };

            await repository.AddFindingRegulatoryProvisionAsync(
                association);

            var byFinding =
                await repository.GetFindingRegulatoryProvisionsAsync(
                    finding.Id);
            var byProvision =
                await repository.GetFindingRegulatoryProvisionsAsync(
                    provision.Id);

            AssertAssociation(association, Assert.Single(byFinding));
            AssertAssociation(association, Assert.Single(byProvision));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsMissingFindingOrProvision()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteFindingRepository(databasePath);
            await repository.InitializeAsync();

            var (finding, provision) =
                await SeedFindingAndProvisionAsync(databasePath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddFindingRegulatoryProvisionAsync(
                    new FindingRegulatoryProvision
                    {
                        FindingId = new FindingId("missing-finding"),
                        RegulatoryProvisionId = provision.Id,
                        Role = FindingTraceabilityRoles.Supporting
                    }));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.AddFindingRegulatoryProvisionAsync(
                    new FindingRegulatoryProvision
                    {
                        FindingId = finding.Id,
                        RegulatoryProvisionId =
                            new RegulatoryProvisionId("missing-provision"),
                        Role = FindingTraceabilityRoles.Supporting
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RejectsInvalidTraceabilityRole()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteFindingRepository(databasePath);
            await repository.InitializeAsync();

            var (finding, provision) =
                await SeedFindingAndProvisionAsync(databasePath);

            await Assert.ThrowsAsync<ArgumentException>(
                () => repository.AddFindingRegulatoryProvisionAsync(
                    new FindingRegulatoryProvision
                    {
                        FindingId = finding.Id,
                        RegulatoryProvisionId = provision.Id,
                        Role = "Invalid"
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static async Task<(Finding Finding, RegulatoryProvision Provision)>
        SeedFindingAndProvisionAsync(string databasePath)
    {
        var veteran = new Veteran
        {
            Id = new VeteranId("veteran-finding-regulatory")
        };
        await new SqliteVeteranRepository(databasePath)
            .AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-finding-regulatory"),
            VeteranId = veteran.Id
        };
        await new SqliteClaimRepository(databasePath)
            .AddClaimAsync(claim);

        var claimIssue = new ClaimIssue
        {
            Id = new ClaimIssueId("claim-issue-finding-regulatory"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };
        await new SqliteClaimIssueRepository(databasePath)
            .AddClaimIssueAsync(claimIssue);

        var finding = new Finding
        {
            Id = new FindingId("finding-regulatory"),
            ClaimIssueId = claimIssue.Id,
            RequirementId = null,
            Outcome = FindingOutcomes.Favorable,
            Description = "Evidence supports the finding."
        };
        await new SqliteFindingRepository(databasePath)
            .AddFindingAsync(finding);

        var authority = new RegulatoryAuthority
        {
            Id = new RegulatoryAuthorityId("authority-finding-regulatory"),
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Finding Traceability Authority"
        };
        var provision = new RegulatoryProvision
        {
            Id = new RegulatoryProvisionId("provision-finding-regulatory"),
            RegulatoryAuthorityId = authority.Id,
            ProvisionType = "Requirement",
            Citation = "38 CFR Test"
        };

        var regulatory =
            new SqliteRegulatoryRepository(databasePath);
        await regulatory.AddRegulatoryAuthorityAsync(authority);
        await regulatory.AddRegulatoryProvisionAsync(provision);

        return (finding, provision);
    }

    private static void AssertAssociation(
        FindingRegulatoryProvision expected,
        FindingRegulatoryProvision actual)
    {
        Assert.Equal(expected.FindingId, actual.FindingId);
        Assert.Equal(
            expected.RegulatoryProvisionId,
            actual.RegulatoryProvisionId);
        Assert.Equal(expected.Role, actual.Role);
    }
}

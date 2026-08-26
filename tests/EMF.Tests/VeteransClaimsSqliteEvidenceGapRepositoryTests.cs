using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceGapRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsEvidenceGap()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceGapRepository(databasePath);

            await repository.InitializeAsync();

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

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var authorityId =
                new RegulatoryAuthorityId("authority-001");

            var provisionId =
                new RegulatoryProvisionId("provision-001");

            var requirementId =
                new RequirementId("requirement-001");

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.AddRegulatoryAuthorityAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Test Authority"
                });

            await regulatory.AddRegulatoryProvisionAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId = authorityId,
                    ProvisionType = "Requirement",
                    Citation = "38 CFR Test"
                });

            await regulatory.AddRequirementAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = "Test requirement"
                });

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirementId,
                Description = "Missing supporting evidence.",
                Status = EvidenceGapStatuses.Open
            };

            await repository.AddEvidenceGapAsync(gap);

            var stored =
                await repository.GetEvidenceGapAsync(gap.Id);

            var byIssue =
                await repository.GetEvidenceGapsAsync(
                    issue.Id);

            var byRequirement =
                await repository.GetEvidenceGapsAsync(
                    requirementId);

            Assert.NotNull(stored);
            Assert.Equal(gap.Id, stored!.Id);
            Assert.Equal(gap.ClaimIssueId, stored.ClaimIssueId);
            Assert.Equal(gap.RequirementId, stored.RequirementId);
            Assert.Equal(gap.Description, stored.Description);
            Assert.Equal(gap.Status, stored.Status);

            await repository.UpdateEvidenceGapStatusAsync(
                gap.Id,
                EvidenceGapStatuses.Resolved);

            var resolved =
                await repository.GetEvidenceGapAsync(gap.Id);

            Assert.NotNull(resolved);
            Assert.Equal(
                EvidenceGapStatuses.Resolved,
                resolved!.Status);

            Assert.Equal(
                EvidenceGapStatuses.Open,
                Assert.Single(byIssue).Status);

            Assert.Equal(
                EvidenceGapStatuses.Open,
                Assert.Single(byRequirement).Status);

            Assert.Equal(
                gap.Id,
                Assert.Single(byIssue).Id);

            Assert.Equal(
                gap.Id,
                Assert.Single(byRequirement).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task Repository_RoundTripsEvidenceGapArtifacts()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceGapRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-artifact-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-artifact-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-artifact-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authorityId =
                new RegulatoryAuthorityId(
                    "authority-artifact-001");

            var provisionId =
                new RegulatoryProvisionId(
                    "provision-artifact-001");

            var requirementId =
                new RequirementId(
                    "requirement-artifact-001");

            await regulatory.AddRegulatoryAuthorityAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Artifact Test Authority"
                });

            await regulatory.AddRegulatoryProvisionAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId = authorityId,
                    ProvisionType = "Requirement",
                    Citation = "38 CFR Artifact Test"
                });

            await regulatory.AddRequirementAsync(
                new EMF.Extensions.VeteransClaims.Regulatory.Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = "Artifact test requirement"
                });

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-artifact-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirementId,
                Description = "Missing artifact evidence."
            };

            await repository.AddEvidenceGapAsync(gap);

            await repository.AddEvidenceGapArtifactAsync(
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gap.Id,
                    ArtifactId = new ArtifactId("artifact-002"),
                    Role = "supporting"
                });

            await repository.AddEvidenceGapArtifactAsync(
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gap.Id,
                    ArtifactId = new ArtifactId("artifact-001"),
                    Role = "primary"
                });

            var stored =
                await repository.GetEvidenceGapArtifactsAsync(
                    gap.Id);

            Assert.Equal(2, stored.Count);

            Assert.Equal(
                new ArtifactId("artifact-001"),
                stored[0].ArtifactId);

            Assert.Equal("primary", stored[0].Role);

            Assert.Equal(
                new ArtifactId("artifact-002"),
                stored[1].ArtifactId);

            Assert.Equal("supporting", stored[1].Role);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}

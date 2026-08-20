using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceDevelopmentPlanRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsEvidenceDevelopmentPlan()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath);

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

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(
                plan);

            var stored =
                await repository.GetEvidenceDevelopmentPlanAsync(
                    plan.Id);

            var byIssue =
                await repository.GetEvidenceDevelopmentPlansAsync(
                    issue.Id);

            Assert.NotNull(stored);
            Assert.Equal(plan.Id, stored!.Id);
            Assert.Equal(
                plan.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                plan.Description,
                stored.Description);

            Assert.Equal(
                plan.Id,
                Assert.Single(byIssue).Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_RoundTripsDevelopmentPlanRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

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

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var association =
                new EvidenceDevelopmentPlanRequirement
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    RequirementId = requirement.Id
                };

            await repository
                .AddEvidenceDevelopmentPlanRequirementAsync(
                    association);

            var stored =
                await repository
                    .GetEvidenceDevelopmentPlanRequirementsAsync(
                        association.EvidenceDevelopmentPlanId);

            var result = Assert.Single(stored);

            Assert.Equal(
                association.EvidenceDevelopmentPlanId,
                result.EvidenceDevelopmentPlanId);

            Assert.Equal(
                association.RequirementId,
                result.RequirementId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task CreateEvidenceDevelopmentPlanAsync_RollsBackWhenGapInsertFails()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-rollback-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-rollback-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-rollback-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-rollback-001"),
                ClaimIssueId = issue.Id,
                Description = "Rollback test."
            };

            var gaps =
                new[]
                {
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = plan.Id,
                        EvidenceGapId =
                            new EvidenceGapId("missing-gap-001")
                    }
                };

            await Assert.ThrowsAnyAsync<Exception>(
                () => repository.CreateEvidenceDevelopmentPlanAsync(
                    plan,
                    gaps));

            var stored =
                await repository.GetEvidenceDevelopmentPlanAsync(
                    plan.Id);

            Assert.Null(stored);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task Repository_RoundTripsDevelopmentPlanEvidenceGap()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-gap-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-gap-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-gap-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-gap-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-gap-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-gap-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-gap-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirement.Id,
                Description = "Missing supporting evidence."
            };

            await new SqliteEvidenceGapRepository(databasePath)
                .AddEvidenceGapAsync(gap);

            var association =
                new EvidenceDevelopmentPlanEvidenceGap
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    EvidenceGapId = gap.Id
                };

            await repository
                .AddEvidenceDevelopmentPlanEvidenceGapAsync(
                    association);

            var stored =
                await repository
                    .GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                        plan.Id);

            var result = Assert.Single(stored);

            Assert.Equal(
                association.EvidenceDevelopmentPlanId,
                result.EvidenceDevelopmentPlanId);

            Assert.Equal(
                association.EvidenceGapId,
                result.EvidenceGapId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task Repository_RoundTripsDevelopmentPlanArtifact()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

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

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-artifact-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop supporting evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var association =
                new EvidenceDevelopmentPlanArtifact
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            "artifact-001"),
                    Role = "Supporting"
                };

            await repository
                .AddEvidenceDevelopmentPlanArtifactAsync(
                    association);

            var stored =
                await repository
                    .GetEvidenceDevelopmentPlanArtifactsAsync(
                        plan.Id);

            var result = Assert.Single(stored);

            Assert.Equal(
                association.EvidenceDevelopmentPlanId,
                result.EvidenceDevelopmentPlanId);

            Assert.Equal(
                association.ArtifactId,
                result.ArtifactId);

            Assert.Equal(
                association.Role,
                result.Role);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task Repository_RoundTripsEvidenceDevelopmentExecution()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-execution-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-execution-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-execution-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-execution-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-execution-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-execution-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-execution-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-execution-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirement.Id,
                Description = "Missing supporting evidence."
            };

            await new SqliteEvidenceGapRepository(databasePath)
                .AddEvidenceGapAsync(gap);

            var execution =
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    EvidenceGapId = gap.Id,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-execution-001")
                };

            await repository
                .AddEvidenceDevelopmentExecutionAsync(execution);

            var stored =
                await repository
                    .GetEvidenceDevelopmentExecutionAsync(
                        plan.Id,
                        gap.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                execution.EvidenceDevelopmentPlanId,
                stored!.EvidenceDevelopmentPlanId);
            Assert.Equal(
                execution.EvidenceGapId,
                stored.EvidenceGapId);
            Assert.Equal(
                execution.WorkflowId,
                stored.WorkflowId);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }





    [Fact]
    public async Task Repository_RejectsDuplicateEvidenceDevelopmentExecution()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceDevelopmentPlanRepository(databasePath);

            await repository.InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-duplicate-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-duplicate-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-duplicate-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-duplicate-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-duplicate-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-duplicate-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-duplicate-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-duplicate-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirement.Id,
                Description = "Missing supporting evidence."
            };

            await new SqliteEvidenceGapRepository(databasePath)
                .AddEvidenceGapAsync(gap);

            var first =
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    EvidenceGapId = gap.Id,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-duplicate-001")
                };

            var second =
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    EvidenceGapId = gap.Id,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-duplicate-002")
                };

            await repository
                .AddEvidenceDevelopmentExecutionAsync(first);

            await Assert.ThrowsAnyAsync<Exception>(
                () => repository
                    .AddEvidenceDevelopmentExecutionAsync(second));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}

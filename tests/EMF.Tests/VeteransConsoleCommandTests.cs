using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransConsoleCommandTests
{
    [Fact]
    public async Task EvidenceDevelop_RequiresArguments()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                ["evidence", "develop"]);

        Assert.Equal(2, exitCode);
    }
    [Fact]
    public async Task EvidenceDevelop_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "develop",
                    "/tmp/emf-missing-veterans.db",
                    "plan-1",
                    "gap-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task EvidenceDevelopSummarize_ParsesOptionalFlag()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "develop",
                    "--summarize",
                    "/tmp/emf-missing-veterans-summary.db",
                    "plan-1",
                    "gap-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task EvidenceDevelop_DoesNotCreateIntelligenceRuntimeWithoutFlag()
    {
        var runtimeCreated = false;

        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "develop",
                    "/tmp/emf-missing-veterans-no-summary.db",
                    "plan-1",
                    "gap-1"
                ],
                () =>
                {
                    runtimeCreated = true;
                    throw new InvalidOperationException();
                });

        Assert.Equal(2, exitCode);
        Assert.False(runtimeCreated);
    }

    [Fact]
    public async Task EvidenceDevelopSummarize_UsesInjectedRuntime()
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
                Id = new VeteranId("veteran-console-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-console-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-console-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan = new EvidenceDevelopmentPlan
            {
                Id = new EvidenceDevelopmentPlanId("plan-console-001"),
                ClaimIssueId = issue.Id,
                Description = "Develop missing evidence."
            };

            await repository.AddEvidenceDevelopmentPlanAsync(plan);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-console-001"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-console-001"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.303"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-console-001"),
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            };

            await regulatory.AddRequirementAsync(requirement);

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-console-001"),
                ClaimIssueId = issue.Id,
                RequirementId = requirement.Id,
                Description = "Missing supporting evidence."
            };

            await new SqliteEvidenceGapRepository(databasePath)
                .AddEvidenceGapAsync(gap);

            var guidance =
                new EvidenceRequirementGuidance
                {
                    Id =
                        new EvidenceRequirementGuidanceId(
                            "guidance-console-001"),
                    RequirementId = requirement.Id,
                    EvidenceClassification =
                        EvidenceClassifications.MedicalOpinion,
                    GuidanceRole =
                        EvidenceGuidanceRoles.SupportsRequirement,
                    Description =
                        "A medical opinion may help support the requirement."
                };

            await new SqliteEvidenceRequirementGuidanceRepository(
                databasePath)
                .AddEvidenceRequirementGuidanceAsync(guidance);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "develop",
                        "--summarize",
                        databasePath,
                        plan.Id.Value,
                        gap.Id.Value
                    ],
                    () => Task.FromResult(
                        new TextSummarizationConsoleRuntime
                        {
                            TextSummarizationCapabilityExecutor =
                                new FakeSummarizationExecutor(),
                            SubjectId = "console-test",
                            ClassificationId =
                                new ProtectionClassificationId(
                                    "confidential"),
                            AuditDatabasePath = "test-audit.db"
                        }));

            Assert.Equal(0, exitCode);

        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class FakeSummarizationExecutor :
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string>
    {
        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextSummarizationRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = "Veterans evidence summary.",
                    RequiresReview = true,
                    Metadata = new IntelligenceExecutionMetadata
                    {
                        CapabilityId = capabilityId,
                        ProviderId =
                            new IntelligenceProviderId("test"),
                        CorrelationId = context.CorrelationId,
                        EngineName = "test",
                        StartedUtc = DateTimeOffset.UtcNow,
                        CompletedUtc = DateTimeOffset.UtcNow
                    }
                });
        }
    }

}

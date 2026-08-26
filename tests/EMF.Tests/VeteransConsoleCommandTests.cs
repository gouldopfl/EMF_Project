using EMF.Persistence.Repositories;
using EMF.Orchestration.Services;
using EMF.ConsoleApplication;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
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
    public async Task EvidenceChecklist_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "checklist",
                    "/tmp/emf-missing-veterans-checklist.db",
                    "issue-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task EvidenceClaim_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "claim",
                    "/tmp/emf-missing-veterans-claim.db",
                    "claim-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task EvidencePrepare_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "prepare",
                    "/tmp/emf-missing-veterans-prepare.db",
                    "issue-1",
                    "plan-1"
                ]);

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
    public async Task EvidenceDevelopSummarizePromote_RequiresReviewer()
    {
        var previous =
            Environment.GetEnvironmentVariable(
                "EMF_REVIEWED_BY");

        var databasePath = Path.GetTempFileName();

        try
        {
            Environment.SetEnvironmentVariable(
                "EMF_REVIEWED_BY",
                null);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "develop",
                        "--summarize",
                        "--promote",
                        databasePath,
                        "plan-1",
                        "gap-1"
                    ]);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EMF_REVIEWED_BY",
                previous);

            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidenceDevelopSummarize_UsesInjectedRuntime()
    {
        var databasePath = Path.GetTempFileName();
        var previousReviewer =
            Environment.GetEnvironmentVariable(
                "EMF_REVIEWED_BY");

        try
        {
            Environment.SetEnvironmentVariable(
                "EMF_REVIEWED_BY",
                "console-reviewer");

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

            var gapRepository =
                new SqliteEvidenceGapRepository(databasePath);

            await gapRepository.AddEvidenceGapAsync(gap);

            await gapRepository.AddEvidenceGapArtifactAsync(
                new EvidenceGapArtifact
                {
                    EvidenceGapId = gap.Id,
                    ArtifactId = new ArtifactId("artifact-console-001"),
                    Role = "supporting"
                });

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
                        "--promote",
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

            var expectedArtifact =
                new TextSummaryEvidenceArtifactFactory()
                    .Create(
                        "Veterans evidence summary.",
                        $"Evidence gap {gap.Id.Value} summary",
                        DateTimeOffset.UtcNow);

            var evidenceRepository =
                new SqliteEvidenceRepository(databasePath);

            var stored =
                await evidenceRepository.GetArtifactAsync(
                    expectedArtifact.Id);

            Assert.NotNull(stored);
            Assert.Equal(
                "text-summary",
                stored!.ArtifactType);

            var provenance =
                Assert.Single(
                    await evidenceRepository
                        .GetProvenanceAsync(
                            expectedArtifact.Id));

            Assert.Equal(
                "EMF.Intelligence",
                provenance.Source);

            var relationship =
                Assert.Single(
                    await evidenceRepository
                        .GetRelationshipsAsync(
                            expectedArtifact.Id));

            Assert.Equal(
                new ArtifactId("artifact-console-001"),
                relationship.TargetArtifactId);

        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EMF_REVIEWED_BY",
                previousReviewer);

            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidencePrepare_CreatesPlanFromMissingRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-prepare-1")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-prepare-1"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-prepare-1"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var connections =
                new SqliteServiceConnectionRepository(databasePath);

            var theory = new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-prepare-1"),
                ClaimIssueId = issue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

            await connections
                .AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-prepare-1"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

            await connections
                .AddServiceConnectionBasisAsync(basis);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-prepare-1"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Relief"
            };

            await regulatory
                .AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-prepare-1"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType =
                    RegulatoryProvisionTypes.Presumption,
                Citation = "38 CFR 3.310"
            };

            await regulatory
                .AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-prepare-1"),
                RegulatoryProvisionId = provision.Id,
                Description =
                    "Secondary service connection requirement"
            };

            await regulatory.AddRequirementAsync(requirement);

            await connections.AddBasisRequirementAsync(
                new ServiceConnectionBasisRequirement
                {
                    ServiceConnectionBasisId = basis.Id,
                    RequirementId = requirement.Id
                });

            var guidance =
                new EvidenceRequirementGuidance
                {
                    Id = new EvidenceRequirementGuidanceId(
                        "guidance-prepare-1"),
                    RequirementId = requirement.Id,
                    EvidenceClassification =
                        EvidenceClassifications.MedicalOpinion,
                    GuidanceRole =
                        EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Medical opinion evidence."
                };

            await new SqliteEvidenceRequirementGuidanceRepository(
                databasePath)
                .AddEvidenceRequirementGuidanceAsync(guidance);

            var planId =
                new EvidenceDevelopmentPlanId("plan-prepare-1");

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "prepare",
                        databasePath,
                        issue.Id.Value,
                        planId.Value
                    ]);

            Assert.Equal(0, exitCode);

            var plans =
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath);

            var stored =
                await plans.GetEvidenceDevelopmentPlanAsync(
                    planId);

            Assert.NotNull(stored);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidenceClaim_ReadsClaimAndIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-claim-console-1")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-console-1"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(
                    new ClaimIssue
                    {
                        Id =
                            new ClaimIssueId(
                                "issue-claim-console-1"),
                        ClaimId = claim.Id,
                        ClaimIssueType =
                            ClaimIssueTypes.ServiceConnection
                    });

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "claim",
                        databasePath,
                        claim.Id.Value
                    ]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidenceChecklist_ReadsOutstandingItems()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var issueId =
                new ClaimIssueId("issue-checklist-console-1");

            var requirementId =
                new RequirementId("requirement-checklist-console-1");

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-checklist-console-1")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-checklist-console-1"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(
                    new ClaimIssue
                    {
                        Id = issueId,
                        ClaimId = claim.Id,
                        ClaimIssueType =
                            ClaimIssueTypes.ServiceConnection
                    });

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            var authority =
                new RegulatoryAuthority
                {
                    Id =
                        new RegulatoryAuthorityId(
                            "authority-checklist-console-1"),
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Veterans Affairs"
                };

            await regulatory.AddRegulatoryAuthorityAsync(
                authority);

            var provision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId(
                            "provision-checklist-console-1"),
                    RegulatoryAuthorityId = authority.Id,
                    ProvisionType =
                        RegulatoryProvisionTypes.Requirement,
                    Citation = "38 CFR"
                };

            await regulatory.AddRegulatoryProvisionAsync(
                provision);

            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provision.Id,
                    Description = "Required element."
                });

            var gapRepository =
                new SqliteEvidenceGapRepository(databasePath);

            await gapRepository.InitializeAsync();

            await gapRepository.AddEvidenceGapAsync(
                new EvidenceGap
                {
                    Id =
                        new EvidenceGapId(
                            "gap-checklist-console-1"),
                    ClaimIssueId = issueId,
                    RequirementId = requirementId,
                    Description = "Missing evidence."
                });

            await new SqliteEvidenceRequirementGuidanceRepository(
                databasePath)
                .AddEvidenceRequirementGuidanceAsync(
                    new EvidenceRequirementGuidance
                    {
                        Id =
                            new EvidenceRequirementGuidanceId(
                                "guidance-checklist-console-1"),
                        RequirementId = requirementId,
                        EvidenceClassification =
                            EvidenceClassifications.MedicalOpinion,
                        GuidanceRole =
                            EvidenceGuidanceRoles.SupportsRequirement,
                        Description = "Medical opinion evidence."
                    });

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "checklist",
                        databasePath,
                        issueId.Value
                    ]);

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
                    },
                    SourceArtifactIds =
                        context.InputArtifactIds.ToArray()
                });
        }
    }

}

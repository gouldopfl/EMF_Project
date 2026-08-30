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
    public async Task DecisionInterpret_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "decision",
                    "interpret",
                    "/tmp/emf-missing-veterans-decision.db",
                    "artifact-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task DecisionHistory_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "decision",
                    "history",
                    "/tmp/emf-missing-decision-history.db",
                    "claim-001"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task DecisionReview_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "decision",
                    "review",
                    "/tmp/emf-missing-decision-review.db",
                    "issue-001"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task DecisionReview_ShowsDisagreement()
    {
        var databasePath =
            await CreateDecisionReviewDatabaseAsync();

        try
        {
            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunDecisionReviewAsync(
                        databasePath,
                        new ClaimIssueId(
                            "issue-decision-review"),
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Contains(
                "Issue Decision: issue-decision-review-history",
                rendered);
            Assert.Contains(
                "VA Decision : va-decision-review",
                rendered);
            Assert.Contains(
                "Decision Date: 2026-08-11",
                rendered);
            Assert.Contains(
                "VA Outcome  : Denied",
                rendered);
            Assert.Contains(
                "EMF Merits  : Favorable",
                rendered);
            Assert.Contains(
                "Recommend   : Granted",
                rendered);
            Assert.Contains(
                "Comparison  : Disagreement",
                rendered);
            Assert.Contains(
                "Needs Review: True",
                rendered);
            Assert.Contains(
                "Merits      : Favorable",
                rendered);
            Assert.Contains(
                "Theory      : Secondary",
                rendered);
            Assert.Contains(
                "Outcome     : Favorable",
                rendered);
            Assert.Contains(
                "Basis       : basis-decision-review",
                rendered);
            Assert.Contains(
                "Basis Result: Favorable",
                rendered);
            Assert.Contains(
                "Requirement : requirement-decision-review",
                rendered);
            Assert.Contains(
                "Req Result  : Favorable",
                rendered);
            Assert.Contains(
                "Finding     : finding-decision-review",
                rendered);
            Assert.Contains(
                "Find Result : Favorable",
                rendered);
            Assert.Contains(
                "Description : Requirement supported.",
                rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task DecisionInterpret_InterpretsTextArtifact()
    {
        var databasePath = Path.GetTempFileName();
        var contentPath =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-decision-content-{Guid.NewGuid():N}");

        var previousContentPath =
            Environment.GetEnvironmentVariable(
                "EMF_ARTIFACT_CONTENT_PATH");

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var evidenceRepository =
                new SqliteEvidenceRepository(databasePath);

            await evidenceRepository.InitializeAsync();

            var artifact =
                new EMF.Core.Models.Artifact
                {
                    Id =
                        new ArtifactId(
                            "artifact-decision-console-001"),
                    Name = "decision.txt",
                    ArtifactType = "file",
                    Metadata =
                        new Dictionary<string, object>
                        {
                            [EMF.Core.Models.ArtifactMetadataKeys.FileExtension] =
                                ".txt"
                        }
                };

            await evidenceRepository.AddArtifactAsync(artifact);

            var contentStore =
                new EMF.Persistence.Storage.FileSystemArtifactContentStore(
                    contentPath);

            await contentStore.WriteAsync(
                artifact.Id,
                System.Text.Encoding.UTF8.GetBytes(
                    "VA decision: Sleep apnea is granted."));

            var extractedText =
                await ArtifactTextExtractionFactory
                    .Create(
                        evidenceRepository,
                        contentStore)
                    .ExtractTextAsync(artifact.Id);

            Assert.Equal(
                "VA decision: Sleep apnea is granted.",
                extractedText);

            Environment.SetEnvironmentVariable(
                "EMF_ARTIFACT_CONTENT_PATH",
                contentPath);

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunDecisionInterpretAsync(
                        databasePath,
                        artifact.Id,
                        () => Task.FromResult(
                        new TextSummarizationConsoleRuntime
                        {
                            TextSummarizationCapabilityExecutor =
                                new FakeSummarizationExecutor(),
                            TextStructuredExtractionCapabilityExecutor =
                                new FakeStructuredExtractionExecutor(),
                            SubjectId = "console-test",
                            ClassificationId =
                                new ProtectionClassificationId(
                                    "confidential"),
                            AuditDatabasePath = "test-audit.db"
                        }),
                        contentStore,
                        output);

            Assert.Equal(0, exitCode);
            var rendered = output.ToString();

            Assert.Contains(
                "Requires Review: True",
                rendered);

            Assert.Contains(
                "Favorable   : Sleep apnea is documented.",
                rendered);

            Assert.Contains(
                "Regulation  : 38 CFR 3.310",
                rendered);

            Assert.Contains(
                "Evidence    : VA sleep study",
                rendered);

            Assert.Contains(
                "Source      : Sleep apnea is granted.",
                rendered);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "EMF_ARTIFACT_CONTENT_PATH",
                previousContentPath);

            File.Delete(databasePath);

            if (Directory.Exists(contentPath))
                Directory.Delete(
                    contentPath,
                    recursive: true);
        }
    }

    [Fact]
    public async Task DecisionHistory_ShowsProcessingHistory()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-history-001")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-history-001"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var attempts =
                new SqliteVaDecisionDocumentProcessingAttemptRepository(
                    databasePath);

            await attempts.AddAsync(
                new VaDecisionDocumentProcessingAttempt
                {
                    ClaimId = claim.Id,
                    ArtifactId =
                        new ArtifactId("artifact-history-001"),
                    ProcessedAt =
                        new DateTimeOffset(
                            2026, 8, 29, 12, 0, 0,
                            TimeSpan.Zero),
                    VaDecisionId = null,
                    Matches = []
                });

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunDecisionHistoryAsync(
                        databasePath,
                        claim.Id,
                        output);

            Assert.Equal(0, exitCode);

            var rendered = output.ToString();

            Assert.Contains(
                "Artifact    : artifact-history-001",
                rendered);

            Assert.Contains(
                "Persisted   : False",
                rendered);

            Assert.Contains(
                "Matched     : 0",
                rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
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
    public async Task AdjudicationAssess_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "adjudication",
                    "assess",
                    "/tmp/emf-missing-veterans-adjudication.db",
                    "issue-1"
                ]);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task AdjudicationAssess_ReportsReadyWhenNothingIsOutstanding()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-adjudication-1")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-adjudication-1"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-adjudication-1"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "adjudication",
                        "assess",
                        databasePath,
                        issue.Id.Value
                    ]);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AdjudicationAssess_ReportsPersistedTimeline()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-timeline")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-timeline"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-timeline"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var claimedCondition =
                new EMF.Extensions.VeteransClaims.Models.Conditions.ClaimedCondition
                {
                    Id =
                        new ClaimedConditionId(
                            "claimed-condition-timeline"),
                    ClaimIssueId = issue.Id,
                    Name = "Sleep apnea"
                };

            await new SqliteConditionRepository(databasePath)
                .AddClaimedConditionAsync(claimedCondition);

            var submission = new Submission
            {
                Id = new SubmissionId("submission-timeline"),
                ClaimId = claim.Id,
                SubmissionType = SubmissionTypes.SupplementalClaim
            };

            await new SqliteSubmissionRepository(databasePath)
                .AddSubmissionAsync(
                    submission,
                    new[] { issue.Id });

            var decision = new VaDecision
            {
                Id = new VaDecisionId("decision-timeline"),
                DecisionDate =
                    new DateTimeOffset(
                        2026, 8, 11,
                        0, 0, 0,
                        TimeSpan.Zero)
            };

            var issueDecision = new IssueDecision
            {
                Id = new IssueDecisionId("issue-decision-timeline"),
                VaDecisionId = decision.Id,
                ClaimIssueId = issue.Id,
                Outcome = IssueDecisionOutcomes.Denied
            };

            await new SqliteVaDecisionRepository(databasePath)
                .AddDecisionAsync(
                    decision,
                    new[] { issueDecision },
                    new[]
                    {
                        new IssueDecisionSubmission
                        {
                            IssueDecisionId = issueDecision.Id,
                            SubmissionId = submission.Id
                        }
                    });

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunAdjudicationAssessmentAsync(
                        databasePath,
                        issue.Id,
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);

            Assert.Contains(
                "Claimed Condition: claimed-condition-timeline (Sleep apnea)",
                rendered);

            Assert.Contains(
                "Attention   : False",
                rendered);

            Assert.Contains(
                "Follow Up   : False",
                rendered);

            Assert.Contains(
                "Merits      : Unresolved",
                rendered);

            Assert.Contains(
                "Recommend   : None",
                rendered);

            Assert.Contains(
                "Review History: 1",
                rendered);

            Assert.Contains(
                "- NotComparable Review=False",
                rendered);

            Assert.Contains(
                "Timeline    : 1",
                rendered);

            Assert.Contains(
                "VaDecision [Denied]: SupplementalClaim",
                rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AdjudicationAssess_ReportsBlockedRequirement()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-adjudication-blocked")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-adjudication-blocked"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-adjudication-blocked"),
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
                Id = new ServiceConnectionTheoryId("theory-adjudication-blocked"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

            await connections.AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-adjudication-blocked"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

            await connections.AddServiceConnectionBasisAsync(basis);

            var serviceConnectedCondition =
                new EMF.Extensions.VeteransClaims.Models.Conditions.MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            "medical-condition-adjudication-blocked"),
                    Name = "Posttraumatic stress disorder"
                };

            var conditions =
                new SqliteConditionRepository(databasePath);

            await conditions.AddMedicalConditionAsync(
                serviceConnectedCondition);

            await conditions.AddVeteranMedicalConditionAsync(
                new EMF.Extensions.VeteransClaims.Models.Conditions.VeteranMedicalCondition
                {
                    VeteranId = veteran.Id,
                    MedicalConditionId =
                        serviceConnectedCondition.Id
                });

            await connections
                .AddBasisServiceConnectedConditionAsync(
                    new ServiceConnectionBasisServiceConnectedCondition
                    {
                        ServiceConnectionBasisId = basis.Id,
                        ServiceConnectedConditionId =
                            serviceConnectedCondition.Id
                    });

            var serviceEvent =
                new ServiceEvent
                {
                    Id =
                        new ServiceEventId(
                            "service-event-adjudication-blocked"),
                    VeteranId = veteran.Id,
                    Description = "Documented duty event"
                };

            await new SqliteServiceHistoryRepository(
                databasePath)
                .AddServiceEventAsync(serviceEvent);

            await connections
                .AddBasisServiceEventAsync(
                    new ServiceConnectionBasisServiceEvent
                    {
                        ServiceConnectionBasisId = basis.Id,
                        ServiceEventId = serviceEvent.Id
                    });

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-adjudication-blocked"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Relief"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-adjudication-blocked"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Presumption,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-adjudication-blocked"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary service connection requirement"
            };

            await regulatory.AddRequirementAsync(requirement);

            await connections.AddBasisRequirementAsync(
                new ServiceConnectionBasisRequirement
                {
                    ServiceConnectionBasisId = basis.Id,
                    RequirementId = requirement.Id
                });

            await new SqliteEvidenceRequirementGuidanceRepository(
                databasePath)
                .AddEvidenceRequirementGuidanceAsync(
                    new EvidenceRequirementGuidance
                    {
                        Id =
                            new EvidenceRequirementGuidanceId(
                                "guidance-adjudication-blocked"),
                        RequirementId = requirement.Id,
                        EvidenceClassification =
                            EvidenceClassifications.MedicalOpinion,
                        GuidanceRole =
                            EvidenceGuidanceRoles.SupportsRequirement,
                        Description = "Medical opinion evidence."
                    });

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunAdjudicationAssessmentAsync(
                        databasePath,
                        issue.Id,
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);

            Assert.Contains(
                "Evidence Checklists: 0",
                rendered);

            Assert.Contains(
                "Evidence Outstanding: False",
                rendered);

            Assert.Contains(
                "Development Plans: 0",
                rendered);

            Assert.Contains(
                "Theory      : Secondary",
                rendered);

            Assert.Contains(
                "Theory ID   : theory-adjudication-blocked",
                rendered);

            Assert.Contains(
                "Outcome     : Unresolved",
                rendered);

            Assert.Contains(
                "Basis       : basis-adjudication-blocked",
                rendered);

            Assert.Contains(
                "Basis Result: Unresolved",
                rendered);

            Assert.Contains(
                "Service Connected: medical-condition-adjudication-blocked (Posttraumatic stress disorder)",
                rendered);

            Assert.Contains(
                "Service Event: service-event-adjudication-blocked (Documented duty event)",
                rendered);

            Assert.Contains(
                "Requirement : requirement-adjudication-blocked",
                rendered);

            Assert.Contains(
                "Req Result  : Unresolved",
                rendered);

            Assert.Contains(
                "Description : Secondary service connection requirement",
                rendered);

            Assert.Contains(
                "Provision   : provision-adjudication-blocked",
                rendered);

            Assert.Contains(
                "Citation    : 38 CFR 3.310",
                rendered);

            Assert.Contains(
                "Evidence Matched : 0",
                rendered);

            Assert.Contains(
                "Evidence Missing : 1",
                rendered);

            Assert.Contains(
                "Development Items: 1",
                rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task EvidenceExecute_RejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence",
                    "execute",
                    "/tmp/emf-missing-veterans-execute.db",
                    "plan-1"
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
                            TextStructuredExtractionCapabilityExecutor =
                                new FakeStructuredExtractionExecutor(),
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
    public async Task EvidenceExecute_ExecutesPreparedPlan()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-execute-1")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-execute-1"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-execute-1"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-execute-1"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Relief"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-execute-1"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType =
                    RegulatoryProvisionTypes.Presumption,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = new RequirementId("requirement-execute-1"),
                    RegulatoryProvisionId = provision.Id,
                    Description = "Execution test requirement"
                });

            var gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-execute-1"),
                ClaimIssueId = issue.Id,
                RequirementId =
                    new RequirementId("requirement-execute-1"),
                Description = "Missing evidence."
            };

            var gaps =
                new SqliteEvidenceGapRepository(databasePath);

            await gaps.AddEvidenceGapAsync(gap);

            var plan = new EvidenceDevelopmentPlan
            {
                Id =
                    new EvidenceDevelopmentPlanId(
                        "plan-execute-1"),
                ClaimIssueId = issue.Id,
                Description = "Develop evidence."
            };

            var plans =
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath);

            await plans.InitializeAsync();

            await plans.CreateEvidenceDevelopmentPlanAsync(
                plan,
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = plan.Id,
                        EvidenceGapId = gap.Id
                    }
                ]);

            var guidance =
                new EvidenceRequirementGuidance
                {
                    Id = new EvidenceRequirementGuidanceId(
                        "guidance-execute-1"),
                    RequirementId = gap.RequirementId,
                    EvidenceClassification =
                        EvidenceClassifications.MedicalOpinion,
                    GuidanceRole =
                        EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Medical opinion evidence."
                };

            await new SqliteEvidenceRequirementGuidanceRepository(
                databasePath)
                .AddEvidenceRequirementGuidanceAsync(guidance);

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "evidence",
                        "execute",
                        databasePath,
                        plan.Id.Value
                    ]);

            Assert.Equal(0, exitCode);

            var execution =
                await plans.GetEvidenceDevelopmentExecutionAsync(
                    plan.Id,
                    gap.Id);

            Assert.NotNull(execution);
            Assert.Equal(plan.Id,
                execution!.EvidenceDevelopmentPlanId);
            Assert.Equal(gap.Id, execution.EvidenceGapId);
        }
        finally
        {
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
    public async Task EvidencePlan_ReportsPlanSummary()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-plan-summary")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-plan-summary"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue =
                new ClaimIssue
                {
                    Id = new ClaimIssueId("issue-plan-summary"),
                    ClaimId = claim.Id,
                    ClaimIssueType =
                        ClaimIssueTypes.ServiceConnection
                };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var plan =
                new EvidenceDevelopmentPlan
                {
                    Id =
                        new EvidenceDevelopmentPlanId(
                            "plan-summary-1"),
                    ClaimIssueId = issue.Id,
                    Description = "Develop supporting evidence."
                };

            var plans =
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath);

            await plans.InitializeAsync();

            var authorityId =
                new RegulatoryAuthorityId(
                    "authority-plan-summary");

            var provisionId =
                new RegulatoryProvisionId(
                    "provision-plan-summary");

            var requirementId =
                new RequirementId(
                    "requirement-summary-1");

            var regulatory =
                new SqliteRegulatoryRepository(databasePath);

            await regulatory.AddRegulatoryAuthorityAsync(
                new RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "Regulation",
                    Citation = "38 CFR",
                    Title = "Test Authority"
                });

            await regulatory.AddRegulatoryProvisionAsync(
                new RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId = authorityId,
                    ProvisionType = "Requirement",
                    Citation = "38 CFR Test"
                });

            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = "Test requirement"
                });

            var gap =
                new EvidenceGap
                {
                    Id = new EvidenceGapId("gap-summary-1"),
                    ClaimIssueId = issue.Id,
                    RequirementId = requirementId,
                    Description = "Missing supporting evidence.",
                    Status = EvidenceGapStatuses.Open
                };

            await new SqliteEvidenceGapRepository(databasePath)
                .AddEvidenceGapAsync(gap);

            await plans.CreateEvidenceDevelopmentPlanAsync(
                plan,
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = plan.Id,
                        EvidenceGapId = gap.Id
                    }
                ]);

            await plans.AddEvidenceDevelopmentPlanRequirementAsync(
                new EvidenceDevelopmentPlanRequirement
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    RequirementId = requirementId
                });

            await plans.AddEvidenceDevelopmentPlanArtifactAsync(
                new EvidenceDevelopmentPlanArtifact
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            "artifact-summary-1"),
                    Role = "Supporting"
                });

            await plans.AddEvidenceDevelopmentExecutionAsync(
                new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId = plan.Id,
                    EvidenceGapId = gap.Id,
                    WorkflowId =
                        new EMF.Core.Models.Identities.WorkflowId(
                            "workflow-summary-1")
                });

            await plans.AddEvidenceDevelopmentResultAsync(
                new EvidenceDevelopmentResult
                {
                    EvidenceGapId = gap.Id,
                    RequirementId = requirementId,
                    EvidenceGuidance = [],
                    MatchingGuidanceItemCount = 1,
                    MissingGuidanceItemCount = 0,
                    ResultingGapStatus =
                        EvidenceGapStatuses.Resolved
                });

            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidenceDevelopmentPlanAsync(
                        databasePath,
                        plan.Id,
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);

            Assert.Contains(
                "Plan ID     : plan-summary-1",
                rendered);

            Assert.Contains(
                "Claim Issue : issue-plan-summary",
                rendered);

            Assert.Contains(
                "Description : Develop supporting evidence.",
                rendered);

            Assert.Contains(
                "Status      : RequiresDevelopment",
                rendered);

            Assert.Contains(
                "Requirements: 1",
                rendered);

            Assert.Contains(
                "Plan Requirement: requirement-summary-1",
                rendered);

            Assert.Contains(
                "Evidence Gaps: 1",
                rendered);

            Assert.Contains(
                "Gap         : gap-summary-1",
                rendered);

            Assert.Contains(
                "Requirement : requirement-summary-1",
                rendered);

            Assert.Contains(
                "Gap Status  : Open",
                rendered);

            Assert.Contains(
                "Gap Detail  : Missing supporting evidence.",
                rendered);

            Assert.Contains(
                "Artifacts   : 1",
                rendered);

            Assert.Contains(
                "Plan Artifact: artifact-summary-1 (Supporting)",
                rendered);

            Assert.Contains(
                "Executions  : 1",
                rendered);

            Assert.Contains(
                "Execution   : gap-summary-1 -> workflow-summary-1",
                rendered);

            Assert.Contains(
                "Results     : 1",
                rendered);

            Assert.Contains(
                "Result      : gap-summary-1",
                rendered);

            Assert.Contains(
                "Result Req  : requirement-summary-1",
                rendered);

            Assert.Contains(
                "Matched     : 1",
                rendered);

            Assert.Contains(
                "Missing     : 0",
                rendered);

            Assert.Contains(
                "Result Status: Resolved",
                rendered);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AdjudicationClaim_ReadsClaimAndIssues()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-adjudication-claim")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = new ClaimId("claim-adjudication-console"),
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(
                    new ClaimIssue
                    {
                        Id = new ClaimIssueId(
                            "issue-adjudication-console"),
                        ClaimId = claim.Id,
                        ClaimIssueType =
                            ClaimIssueTypes.ServiceConnection
                    });

            var exitCode =
                await VeteransConsoleCommand.RunAsync(
                    [
                        "adjudication",
                        "claim",
                        databasePath,
                        claim.Id.Value
                    ]);

            Assert.Equal(0, exitCode);

            using var output = new StringWriter();

            var renderedExitCode =
                await VeteransConsoleCommand
                    .RunClaimAdjudicationAssessmentAsync(
                        databasePath,
                        claim.Id,
                        output);

            Assert.Equal(0, renderedExitCode);

            var rendered = output.ToString();

            Assert.Contains(
                $"Claim       : {claim.Id.Value}",
                rendered);

            Assert.Contains(
                "Attention   : False",
                rendered);

            Assert.Contains(
                "Follow Up   : False",
                rendered);

            Assert.Contains(
                "Issue       : issue-adjudication-console",
                rendered);

            Assert.Contains(
                "  Ready     : True",
                rendered);

            Assert.Contains(
                "  Attention : False",
                rendered);

            Assert.Contains(
                "  Follow Up : False",
                rendered);

            Assert.Contains(
                "  Merits    : Unresolved",
                rendered);

            Assert.Contains(
                "  Recommend : None",
                rendered);


            Assert.Contains(
                "  Reviews   : 0",
                rendered);


            Assert.Contains(
                "  Review Req: 0",
                rendered);
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


    private sealed class FakeStructuredExtractionExecutor :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output =
                        """
                        {
                          "decisionDate": "2026-08-29T00:00:00Z",
                          "issueDecisions": [{
                            "issueDescription": "Sleep apnea",
                            "outcome": "Granted",
                            "rationale": "The evidence supports the claimed condition.",
                            "favorableFindings": [
                              "Sleep apnea is documented."
                            ],
                            "adverseFindings": [],
                            "citedRegulations": [
                              "38 CFR 3.310"
                            ],
                            "referencedEvidence": [
                              "VA sleep study"
                            ],
                            "sourceExcerpts": [{
                              "text": "Sleep apnea is granted.",
                              "startOffset": 0,
                              "length": 25
                            }]
                          }]
                        }
                        """,
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
    private static async Task<string>
        CreateDecisionReviewDatabaseAsync()
    {
        var path = Path.GetTempFileName();

        await new VeteransClaimsSqliteSchema(path)
            .InitializeAsync();

        var veteran = new Veteran
        {
            Id = new VeteranId("veteran-decision-review")
        };

        await new SqliteVeteranRepository(path)
            .AddVeteranAsync(veteran);

        var claim = new Claim
        {
            Id = new ClaimId("claim-decision-review"),
            VeteranId = veteran.Id
        };

        await new SqliteClaimRepository(path)
            .AddClaimAsync(claim);

        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-decision-review"),
            ClaimId = claim.Id,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        await new SqliteClaimIssueRepository(path)
            .AddClaimIssueAsync(issue);

        var connections =
            new SqliteServiceConnectionRepository(path);

        var theory = new ServiceConnectionTheory
        {
            Id =
                new ServiceConnectionTheoryId(
                    "theory-decision-review"),
            ClaimIssueId = issue.Id,
            TheoryType =
                ServiceConnectionTheoryTypes.Secondary
        };

        await connections
            .AddServiceConnectionTheoryAsync(theory);

        var basis = new ServiceConnectionBasis
        {
            Id =
                new ServiceConnectionBasisId(
                    "basis-decision-review"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory.Id
        };

        await connections
            .AddServiceConnectionBasisAsync(basis);

        var regulatory =
            new SqliteRegulatoryRepository(path);

        var authority = new RegulatoryAuthority
        {
            Id =
                new RegulatoryAuthorityId(
                    "authority-decision-review"),
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Veterans Benefits"
        };

        await regulatory
            .AddRegulatoryAuthorityAsync(authority);

        var provision = new RegulatoryProvision
        {
            Id =
                new RegulatoryProvisionId(
                    "provision-decision-review"),
            RegulatoryAuthorityId = authority.Id,
            ProvisionType =
                RegulatoryProvisionTypes.Presumption,
            Citation = "38 CFR 3.310"
        };

        await regulatory
            .AddRegulatoryProvisionAsync(provision);

        var requirement = new Requirement
        {
            Id =
                new RequirementId(
                    "requirement-decision-review"),
            RegulatoryProvisionId = provision.Id,
            Description =
                "Secondary service connection requirement"
        };

        await regulatory
            .AddRequirementAsync(requirement);

        await connections
            .AddBasisRequirementAsync(
                new ServiceConnectionBasisRequirement
                {
                    ServiceConnectionBasisId = basis.Id,
                    RequirementId = requirement.Id
                });

        await new SqliteFindingRepository(path)
            .AddFindingAsync(
                new Finding
                {
                    Id =
                        new FindingId(
                            "finding-decision-review"),
                    ClaimIssueId = issue.Id,
                    RequirementId = requirement.Id,
                    Outcome = FindingOutcomes.Favorable,
                    Description = "Requirement supported."
                });

        var decision = new VaDecision
        {
            Id = new VaDecisionId("va-decision-review"),
            DecisionDate =
                new DateTimeOffset(
                    2026, 8, 11,
                    0, 0, 0,
                    TimeSpan.Zero)
        };

        var issueDecision = new IssueDecision
        {
            Id =
                new IssueDecisionId(
                    "issue-decision-review-history"),
            VaDecisionId = decision.Id,
            ClaimIssueId = issue.Id,
            Outcome = IssueDecisionOutcomes.Denied
        };

        await new SqliteVaDecisionRepository(path)
            .AddDecisionAsync(
                decision,
                new[] { issueDecision },
                []);

        return path;
    }

}

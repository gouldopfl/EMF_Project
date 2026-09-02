using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageIntelligenceServiceTests
{
    [Fact]
    public async Task SummarizeAsync_UsesFactsAndReviewerGuardrails()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-intelligence-1"),
            ClaimId = new ClaimId("claim-intelligence-1"),
            ClaimIssueType = "ServiceConnection"
        };

        var details = new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [],
            ServiceConnectedConditions = [],
            ServiceEvents = [],
            Requirements = [],
            Evidence =
                new ClaimIssueEvidenceDetails
                {
                    ClaimIssue = issue,
                    Checklist =
                        new ClaimIssueEvidenceChecklist
                        {
                            ClaimIssueId = issue.Id,
                            RequirementChecklists = []
                        },
                    DevelopmentPlans = []
                },
            Timeline = []
        };

        var context = new IntelligenceExecutionContext(
            "reviewer-package-steward",
            new IntelligenceCorrelationId("reviewer-operation-1"),
            new ProtectionClassificationId("confidential"),
            [new ArtifactId("source-artifact-1")]);

        var executor =
            new RecordingTextSummarizationExecutor();
        var service =
            new VeteransReviewerPackageIntelligenceService(
                executor);

        var result =
            await service.SummarizeAsync(
                details,
                context);

        Assert.True(result.Success);
        Assert.Equal("Reviewer summary", result.Output);

        Assert.Contains(
            "Prepare a factual reviewer package summary.",
            executor.Request!.Text);

        Assert.Contains(
            "Do not make medical, legal, or adjudicative conclusions.",
            executor.Request.Text);

        Assert.Contains(
            "Claim Issue: issue-intelligence-1",
            executor.Request.Text);

        Assert.Equal(
            context.SubjectId,
            executor.Context!.SubjectId);

        Assert.Equal(
            context.CorrelationId,
            executor.Context.CorrelationId);

        Assert.Equal(
            context.InputArtifactIds,
            executor.Context.InputArtifactIds);
    }

    private static ClaimIssueAdjudicationDetails
        CreateDetails()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-intelligence-test"),
            ClaimId = new ClaimId("claim-intelligence-test"),
            ClaimIssueType = "ServiceConnection"
        };

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [],
            ServiceConnectedConditions = [],
            ServiceEvents = [],
            Requirements = [],
            Evidence =
                new ClaimIssueEvidenceDetails
                {
                    ClaimIssue = issue,
                    Checklist =
                        new ClaimIssueEvidenceChecklist
                        {
                            ClaimIssueId = issue.Id,
                            RequirementChecklists = []
                        },
                    DevelopmentPlans = []
                },
            Timeline = []
        };
    }


    [Fact]
    public async Task SummarizeAsync_RejectsEmptySuccessfulOutput()
    {
        var executor =
            new RecordingTextSummarizationExecutor
            {
                Success = true,
                Output = "   "
            };

        var service =
            new VeteransReviewerPackageIntelligenceService(
                executor);

        var context = new IntelligenceExecutionContext(
            "reviewer-package-steward",
            new IntelligenceCorrelationId("empty-output"),
            new ProtectionClassificationId("confidential"),
            []);

        var result =
            await service.SummarizeAsync(
                CreateDetails(),
                context);

        Assert.False(result.Success);
    }


    [Fact]
    public void Service_ImplementsReviewerIntelligenceContract()
    {
        IVeteransReviewerPackageIntelligenceService service =
            new VeteransReviewerPackageIntelligenceService(
                new RecordingTextSummarizationExecutor());

        Assert.NotNull(service);
    }

}

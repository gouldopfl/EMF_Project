using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationReadinessServiceTests
{
    [Fact]
    public void Assess_ReturnsReadyWhenNoRequirementsAreOutstanding()
    {
        var details =
            CreateDetails(
                RequirementDetails(
                    "req-1",
                    outstanding: false));

        var result =
            new ClaimIssueAdjudicationReadinessService()
                .Assess(details);

        Assert.True(result.IsReadyForAdjudication);
        Assert.Equal(0, result.OutstandingRequirementCount);
        Assert.Empty(result.BlockingRequirements);
    }

    [Fact]
    public void Assess_IdentifiesBlockingRequirement()
    {
        var satisfied =
            RequirementDetails(
                "req-satisfied",
                outstanding: false);

        var blocking =
            RequirementDetails(
                "req-blocking",
                outstanding: true);

        var result =
            new ClaimIssueAdjudicationReadinessService()
                .Assess(
                    CreateDetails(
                        satisfied,
                        blocking));

        Assert.False(result.IsReadyForAdjudication);
        Assert.Equal(1, result.OutstandingRequirementCount);

        Assert.Equal(
            blocking.Requirement.Id,
            Assert.Single(result.BlockingRequirements)
                .Requirement.Id);


        Assert.Equal(1, result.OutstandingItemCount);

        var item =
            Assert.Single(result.BlockingItems);

        Assert.Equal(
            blocking.Requirement.Id,
            item.RequirementId);

        Assert.Equal(
            EvidenceClassifications.MedicalOpinion,
            item.EvidenceClassification);


        Assert.Equal(
            ClaimIssueAdjudicationBlockerTypes.MissingEvidence,
            item.BlockerType);
    }

    private static ClaimIssueAdjudicationDetails CreateDetails(
        params ServiceConnectionBasisRequirementDetails[] requirements)
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId =
                    new ClaimId("claim-1"),
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [],
            ServiceConnectedConditions = [],
            Requirements = requirements,
            Timeline = [],
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
                }
        };
    }

    private static ServiceConnectionBasisRequirementDetails
        RequirementDetails(
            string id,
            bool outstanding)
    {
        var requirementId =
            new RequirementId(id);

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis =
                new ServiceConnectionBasis
                {
                    Id =
                        new ServiceConnectionBasisId(
                            $"basis-{id}"),
                    ClaimIssueId =
                        new ClaimIssueId("issue-1"),
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId(
                            $"theory-{id}")
                },
            Requirement =
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId(
                            $"provision-{id}"),
                    Description = id
                },
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirementId,
                    Items = []
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items =
                        outstanding
                            ? new[]
                            {
                                new EvidenceDevelopmentChecklistItem
                                {
                                    RequirementId = requirementId,
                                    EvidenceClassification =
                                        EvidenceClassifications.MedicalOpinion,
                                    GuidanceRole =
                                        EvidenceGuidanceRoles.SupportsRequirement,
                                    Description =
                                        "Missing evidence."
                                }
                            }
                            : []
                }
        };
    }
}

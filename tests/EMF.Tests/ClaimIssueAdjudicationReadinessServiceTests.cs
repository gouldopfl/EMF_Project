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
    public void Assess_RejectsRequirementForDifferentClaimIssue()
    {
        var requirement =
            RequirementDetails(
                "req-foreign",
                outstanding: true);

        requirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis =
                    new ServiceConnectionBasis
                    {
                        Id = requirement.Basis.Id,
                        ClaimIssueId =
                            new ClaimIssueId("issue-other"),
                        ServiceConnectionTheoryId =
                            requirement.Basis
                                .ServiceConnectionTheoryId
                    },
                Requirement = requirement.Requirement,
                RegulatoryProvision =
                    requirement.RegulatoryProvision,
                Responsiveness = requirement.Responsiveness,
                DevelopmentChecklist =
                    requirement.DevelopmentChecklist
            };

        var details =
            CreateDetails(requirement);

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueAdjudicationReadinessService()
                        .Assess(details));

        Assert.Equal(
            "Readiness requirement claim issue mismatch.",
            ex.Message);
    }

    [Fact]
    public void Assess_RejectsChecklistForDifferentRequirement()
    {
        var original =
            RequirementDetails(
                "req-1",
                outstanding: true);

        var requirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis = original.Basis,
                Requirement = original.Requirement,
                RegulatoryProvision =
                    original.RegulatoryProvision,
                Responsiveness = original.Responsiveness,
                DevelopmentChecklist =
                    new EvidenceDevelopmentChecklist
                    {
                        RequirementId =
                            new RequirementId("req-other"),
                        Items =
                            original.DevelopmentChecklist.Items
                    }
            };

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueAdjudicationReadinessService()
                        .Assess(CreateDetails(requirement)));

        Assert.Equal(
            "Readiness checklist requirement mismatch.",
            ex.Message);
    }

    [Fact]
    public void Assess_RejectsResponsivenessForDifferentRequirement()
    {
        var original =
            RequirementDetails(
                "req-1",
                outstanding: true);

        var requirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis = original.Basis,
                Requirement = original.Requirement,
                RegulatoryProvision =
                    original.RegulatoryProvision,
                Responsiveness =
                    new RequirementEvidenceResponsivenessAssessment
                    {
                        RequirementId =
                            new RequirementId("req-other"),
                        Items = original.Responsiveness.Items
                    },
                DevelopmentChecklist =
                    original.DevelopmentChecklist
            };

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueAdjudicationReadinessService()
                        .Assess(CreateDetails(requirement)));

        Assert.Equal(
            "Readiness responsiveness requirement mismatch.",
            ex.Message);
    }

    [Fact]
    public void Assess_RejectsChecklistItemForDifferentRequirement()
    {
        var original =
            RequirementDetails(
                "req-1",
                outstanding: true);

        var checklist =
            new EvidenceDevelopmentChecklist
            {
                RequirementId =
                    original.DevelopmentChecklist.RequirementId,
                Items =
                [
                    new EvidenceDevelopmentChecklistItem
                    {
                        RequirementId =
                            new RequirementId("req-other"),
                        EvidenceClassification =
                            EvidenceClassifications.MedicalOpinion,
                        GuidanceRole =
                            EvidenceGuidanceRoles.SupportsRequirement,
                        Description = "Missing evidence."
                    }
                ]
            };

        var requirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis = original.Basis,
                Requirement = original.Requirement,
                RegulatoryProvision =
                    original.RegulatoryProvision,
                Responsiveness = original.Responsiveness,
                DevelopmentChecklist = checklist
            };

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueAdjudicationReadinessService()
                        .Assess(CreateDetails(requirement)));

        Assert.Equal(
            "Readiness checklist item requirement mismatch.",
            ex.Message);
    }

    [Fact]
    public void Assess_RejectsRegulatoryProvisionMismatch()
    {
        var original =
            RequirementDetails(
                "req-1",
                outstanding: true);

        var requirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis = original.Basis,
                Requirement = original.Requirement,
                RegulatoryProvision =
                    new RegulatoryProvision
                    {
                        Id =
                            new RegulatoryProvisionId(
                                "provision-other"),
                        RegulatoryAuthorityId =
                            original.RegulatoryProvision
                                .RegulatoryAuthorityId,
                        ProvisionType =
                            original.RegulatoryProvision
                                .ProvisionType,
                        Citation =
                            original.RegulatoryProvision
                                .Citation
                    },
                Responsiveness = original.Responsiveness,
                DevelopmentChecklist =
                    original.DevelopmentChecklist
            };

        var ex =
            Assert.Throws<InvalidOperationException>(
                () =>
                    new ClaimIssueAdjudicationReadinessService()
                        .Assess(CreateDetails(requirement)));

        Assert.Equal(
            "Readiness regulatory provision mismatch.",
            ex.Message);
    }

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

    [Fact]
    public void Assess_AggregatesMultipleBlockingRequirements()
    {
        var first =
            RequirementDetails(
                "req-first",
                outstanding: true);

        var second =
            RequirementDetails(
                "req-second",
                outstanding: true);

        var result =
            new ClaimIssueAdjudicationReadinessService()
                .Assess(
                    CreateDetails(
                        first,
                        second));

        Assert.False(result.IsReadyForAdjudication);
        Assert.Equal(2, result.OutstandingRequirementCount);
        Assert.Equal(2, result.BlockingRequirements.Count);
        Assert.Equal(2, result.OutstandingItemCount);
        Assert.Equal(2, result.BlockingItems.Count);
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
            ServiceEvents = [],
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
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id =
                        new RegulatoryProvisionId(
                            $"provision-{id}"),
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId(
                            "authority-test"),
                    ProvisionType = "Test",
                    Citation = "38 CFR"
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

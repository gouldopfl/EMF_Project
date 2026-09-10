using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageBasisScopeServiceTests
{
    [Fact]
    public async Task ScopeAsync_ScopesSelectedBasis()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-1"),
            ClaimId = new ClaimId("claim-1"),
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        var theory1 = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-1"),
            ClaimIssueId = issue.Id,
            TheoryType = "Secondary"
        };

        var theory2 = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-2"),
            ClaimIssueId = issue.Id,
            TheoryType = "Secondary"
        };

        var basis1 = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory1.Id
        };

        var basis2 = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-2"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory2.Id
        };

        var requirement1 = CreateRequirementDetails(
            basis1,
            "req-1",
            "provision-1");

        var requirement2 = CreateRequirementDetails(
            basis2,
            "req-2",
            "provision-2");

        var plan1 = new EvidenceDevelopmentPlan
        {
            Id = new EvidenceDevelopmentPlanId("plan-1"),
            ClaimIssueId = issue.Id,
            Description = "Develop requirement 1."
        };

        var plan2 = new EvidenceDevelopmentPlan
        {
            Id = new EvidenceDevelopmentPlanId("plan-2"),
            ClaimIssueId = issue.Id,
            Description = "Develop requirement 2."
        };

        var gap1 = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-1"),
            ClaimIssueId = issue.Id,
            RequirementId = requirement1.Requirement.Id,
            Description = "Missing evidence.",
            Status = EvidenceGapStatuses.Open
        };

        var gap2 = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-2"),
            ClaimIssueId = issue.Id,
            RequirementId = requirement2.Requirement.Id,
            Description = "Missing evidence.",
            Status = EvidenceGapStatuses.Open
        };

        var developmentRepository =
            new FakeDevelopmentRepository(
                new Dictionary<EvidenceDevelopmentPlanId,
                    IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
                {
                    [plan1.Id] =
                    [
                        new EvidenceDevelopmentPlanEvidenceGap
                        {
                            EvidenceDevelopmentPlanId = plan1.Id,
                            EvidenceGapId = gap1.Id
                        }
                    ],
                    [plan2.Id] =
                    [
                        new EvidenceDevelopmentPlanEvidenceGap
                        {
                            EvidenceDevelopmentPlanId = plan2.Id,
                            EvidenceGapId = gap2.Id
                        }
                    ]
                });

        var gapRepository =
            new FakeGapRepository(
                new Dictionary<EvidenceGapId, EvidenceGap>
                {
                    [gap1.Id] = gap1,
                    [gap2.Id] = gap2
                });

        var service =
            new VeteransReviewerPackageBasisScopeService(
                developmentRepository,
                gapRepository);

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ClaimedConditionBases = [],
                ServiceConnectionTheories = [theory1, theory2],
                ServiceConnectionBases = [basis1, basis2],
                ServiceConnectedConditions = [],
                PrescribedMedications = [],
                Exposures = [],
                PreexistingConditions = [],
                Presumptions = [],
                MedicalOpinions = [],
                BasisArtifacts = [],
                ServiceEvents = [],
                Requirements = [requirement1, requirement2],
                Evidence =
                    new ClaimIssueEvidenceDetails
                    {
                        ClaimIssue = issue,
                        Checklist =
                            new ClaimIssueEvidenceChecklist
                            {
                                ClaimIssueId = issue.Id,
                                RequirementChecklists =
                                [
                                    CreateChecklist(requirement1.Requirement.Id),
                                    CreateChecklist(requirement2.Requirement.Id)
                                ]
                            },
                        DevelopmentPlans = [plan1, plan2]
                    },
                Timeline = []
            };

        var developmentDetails =
            new[]
            {
                new VeteransReviewerEvidenceDevelopmentDetails
                {
                    Gap = gap1,
                    Result = null!
                },
                new VeteransReviewerEvidenceDevelopmentDetails
                {
                    Gap = gap2,
                    Result = null!
                }
            };

        var result =
            await service.ScopeAsync(
                details,
                developmentDetails,
                basis1.Id);

        Assert.Equal(
            basis1.Id,
            Assert.Single(result.Details.ServiceConnectionBases).Id);

        Assert.Equal(
            theory1.Id,
            Assert.Single(result.Details.ServiceConnectionTheories).Id);

        Assert.Equal(
            requirement1.Requirement.Id,
            Assert.Single(result.Details.Requirements).Requirement.Id);

        Assert.Equal(
            requirement1.Requirement.Id,
            Assert.Single(
                result.Details.Evidence.Checklist.RequirementChecklists)
                .RequirementId);

        Assert.Equal(
            plan1.Id,
            Assert.Single(result.Details.Evidence.DevelopmentPlans).Id);

        Assert.Equal(
            gap1.Id,
            Assert.Single(result.DevelopmentDetails).Gap.Id);
    }

    [Fact]
    public async Task ScopeAsync_RejectsMissingBasis()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-1"),
            ClaimId = new ClaimId("claim-1"),
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        var service =
            new VeteransReviewerPackageBasisScopeService(
                new FakeDevelopmentRepository(
                    new Dictionary<EvidenceDevelopmentPlanId,
                        IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>()),
                new FakeGapRepository(
                    new Dictionary<EvidenceGapId, EvidenceGap>()));

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ClaimedConditionBases = [],
                ServiceConnectionTheories = [],
                ServiceConnectionBases = [],
                ServiceConnectedConditions = [],
                PrescribedMedications = [],
                Exposures = [],
                PreexistingConditions = [],
                Presumptions = [],
                MedicalOpinions = [],
                BasisArtifacts = [],
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

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ScopeAsync(
                        details,
                        [],
                        new ServiceConnectionBasisId("missing-basis")));

        Assert.Equal(
            "Service-connection basis not found: missing-basis",
            ex.Message);
    }

    [Fact]
    public async Task ScopeAsync_RejectsPlanGapLineageMismatch()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-1"),
            ClaimId = new ClaimId("claim-1"),
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-1"),
            ClaimIssueId = issue.Id,
            TheoryType = "Secondary"
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId = theory.Id
        };

        var requirement =
            CreateRequirementDetails(
                basis,
                "req-1",
                "provision-1");

        var plan = new EvidenceDevelopmentPlan
        {
            Id = new EvidenceDevelopmentPlanId("plan-1"),
            ClaimIssueId = issue.Id,
            Description = "Develop evidence."
        };

        var developmentRepository =
            new FakeDevelopmentRepository(
                new Dictionary<EvidenceDevelopmentPlanId,
                    IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
                {
                    [plan.Id] =
                    [
                        new EvidenceDevelopmentPlanEvidenceGap
                        {
                            EvidenceDevelopmentPlanId =
                                new EvidenceDevelopmentPlanId("wrong-plan"),
                            EvidenceGapId = new EvidenceGapId("gap-1")
                        }
                    ]
                });

        var service =
            new VeteransReviewerPackageBasisScopeService(
                developmentRepository,
                new FakeGapRepository(
                    new Dictionary<EvidenceGapId, EvidenceGap>()));

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ClaimedConditionBases = [],
                ServiceConnectionTheories = [theory],
                ServiceConnectionBases = [basis],
                ServiceConnectedConditions = [],
                PrescribedMedications = [],
                Exposures = [],
                PreexistingConditions = [],
                Presumptions = [],
                MedicalOpinions = [],
                BasisArtifacts = [],
                ServiceEvents = [],
                Requirements = [requirement],
                Evidence =
                    new ClaimIssueEvidenceDetails
                    {
                        ClaimIssue = issue,
                        Checklist =
                            new ClaimIssueEvidenceChecklist
                            {
                                ClaimIssueId = issue.Id,
                                RequirementChecklists =
                                [
                                    CreateChecklist(
                                        requirement.Requirement.Id)
                                ]
                            },
                        DevelopmentPlans = [plan]
                    },
                Timeline = []
            };

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.ScopeAsync(
                        details,
                        [],
                        basis.Id));

        Assert.Equal(
            "Reviewer development plan gap lineage mismatch.",
            ex.Message);
    }

    private static ServiceConnectionBasisRequirementDetails
        CreateRequirementDetails(
            ServiceConnectionBasis basis,
            string requirementId,
            string provisionId)
    {
        var regulatoryProvisionId =
            new RegulatoryProvisionId(provisionId);

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis = basis,
            Requirement =
                new Requirement
                {
                    Id = new RequirementId(requirementId),
                    RegulatoryProvisionId = regulatoryProvisionId,
                    Description = "Medical nexus requirement."
                },
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id = regulatoryProvisionId,
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId("authority-1"),
                    ProvisionType = "Section",
                    Citation = "38 C.F.R. § 3.310"
                },
            Responsiveness = null!,
            DevelopmentChecklist =
                CreateChecklist(
                    new RequirementId(requirementId))
        };
    }

    private static EvidenceDevelopmentChecklist CreateChecklist(
        RequirementId requirementId) =>
        new()
        {
            RequirementId = requirementId,
            Items =
            [
                new EvidenceDevelopmentChecklistItem
                {
                    RequirementId = requirementId,
                    EvidenceClassification = "MedicalOpinion",
                    GuidanceRole = "SupportsRequirement",
                    Description = "Medical opinion needed."
                }
            ]
        };

    private sealed class FakeDevelopmentRepository :
        IEvidenceDevelopmentPlanRepository
    {
        private readonly IReadOnlyDictionary<
            EvidenceDevelopmentPlanId,
            IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            _links;

        public FakeDevelopmentRepository(
            IReadOnlyDictionary<
                EvidenceDevelopmentPlanId,
                IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>> links)
        {
            _links = links;
        }

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default)
        {
            _links.TryGetValue(planId, out var links);

            return Task.FromResult<
                IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>(
                    links ?? []);
        }

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap>
                evidenceGaps,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceDevelopmentPlan?>
            GetEvidenceDevelopmentPlanAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanArtifactAsync(
            EvidenceDevelopmentPlanArtifact artifact,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
            EvidenceDevelopmentPlanEvidenceGap evidenceGap,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanRequirementAsync(
            EvidenceDevelopmentPlanRequirement requirement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGapRepository : IEvidenceGapRepository
    {
        private readonly IReadOnlyDictionary<EvidenceGapId, EvidenceGap>
            _gaps;

        public FakeGapRepository(
            IReadOnlyDictionary<EvidenceGapId, EvidenceGap> gaps)
        {
            _gaps = gaps;
        }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
        {
            _gaps.TryGetValue(evidenceGapId, out var gap);
            return Task.FromResult(gap);
        }

        public Task AddEvidenceGapAsync(
            EvidenceGap evidenceGap,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermProposalCoordinatorTests
{
    [Fact]
    public async Task ProposeAsync_UsesPersistedBasisContextForEachRequirement()
    {
        var details = CreateDetails();
        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(details),
                executor);

        var results =
            await coordinator.ProposeAsync(
                details.ClaimIssue.Id,
                Context());

        Assert.Equal(2, results.Count);
        Assert.Equal(2, executor.Requests.Count);

        Assert.All(
            results,
            result =>
            {
                Assert.Equal(
                    new ServiceConnectionBasisId("basis-osa"),
                    result.BasisId);
                Assert.Equal(
                    new ClaimedConditionId("condition-osa"),
                    result.ClaimedConditionId);
                Assert.Equal(
                    "Obstructive sleep apnea",
                    result.ClaimedCondition);
                Assert.Equal(
                    ["Coronary artery disease", "Major depressive disorder"],
                    result.ServiceConnectedConditions);
                Assert.Single(result.ProposalResult.Proposals);
            });

        Assert.Contains(
            results,
            x => x.RequirementId ==
                new RequirementId("requirement-310-a"));
        Assert.Contains(
            results,
            x => x.RequirementId ==
                new RequirementId("requirement-310-b"));

        Assert.All(
            executor.Requests,
            request =>
            {
                Assert.Contains(
                    "Claimed condition: Obstructive sleep apnea",
                    request.Text);
                Assert.Contains(
                    "- Coronary artery disease",
                    request.Text);
                Assert.Contains(
                    "- Major depressive disorder",
                    request.Text);
            });
    }

    [Fact]
    public async Task ProposeAsync_RejectsMissingClaimIssueDetails()
    {
        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(null),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProposeAsync(
                new ClaimIssueId("issue-missing"),
                Context()));

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task ProposeAsync_RejectsClaimIssueIdentityMismatch()
    {
        var details = CreateDetails();
        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(details),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProposeAsync(
                new ClaimIssueId("issue-other"),
                Context()));

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task ProposeAsync_RejectsAssociationOutsideKnownBasis()
    {
        var details = CreateDetails();
        var claimed = Assert.Single(details.ClaimedConditions);
        var invalidBasis = Basis("basis-other", details.ClaimIssue.Id);

        var invalid = Copy(
            details,
            claimedConditionBases:
            [
                new ServiceConnectionBasisClaimedConditionDetails
                {
                    Basis = invalidBasis,
                    ClaimedCondition = claimed
                }
            ]);

        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(invalid),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProposeAsync(
                details.ClaimIssue.Id,
                Context()));

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task ProposeAsync_RejectsRegulatoryLineageMismatch()
    {
        var details = CreateDetails();
        var requirement = Assert.Single(
            details.Requirements.Where(
                x => x.Requirement.Id ==
                    new RequirementId("requirement-310-a")));

        var invalidRequirement =
            new ServiceConnectionBasisRequirementDetails
            {
                Basis = requirement.Basis,
                Requirement = requirement.Requirement,
                RegulatoryProvision = Provision("provision-other"),
                Responsiveness = requirement.Responsiveness,
                DevelopmentChecklist =
                    requirement.DevelopmentChecklist
            };

        var invalid = Copy(
            details,
            requirements:
                details.Requirements
                    .Select(
                        x => x.Requirement.Id == requirement.Requirement.Id
                            ? invalidRequirement
                            : x)
                    .ToArray());

        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(invalid),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProposeAsync(
                details.ClaimIssue.Id,
                Context()));

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task ProposeAsync_RejectsDuplicateRequirementAssociation()
    {
        var details = CreateDetails();
        var duplicated =
            Copy(
                details,
                requirements:
                    [
                        .. details.Requirements,
                        details.Requirements[0]
                    ]);

        var executor = new RecordingExecutor();
        var coordinator =
            new EvidenceRecognitionTermProposalCoordinator(
                new StubDetailsService(duplicated),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProposeAsync(
                details.ClaimIssue.Id,
                Context()));

        Assert.Empty(executor.Requests);
    }

    private static ClaimIssueAdjudicationDetails CreateDetails()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-osa"),
            ClaimId = new ClaimId("claim-1"),
            ClaimIssueType = "ServiceConnection"
        };

        var basis = Basis("basis-osa", issue.Id);

        var claimed = new ClaimedCondition
        {
            Id = new ClaimedConditionId("condition-osa"),
            ClaimIssueId = issue.Id,
            Name = "Obstructive sleep apnea"
        };

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [claimed],
            ClaimedConditionBases =
            [
                new ServiceConnectionBasisClaimedConditionDetails
                {
                    Basis = basis,
                    ClaimedCondition = claimed
                }
            ],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [basis],
            ServiceConnectedConditions =
            [
                Condition(basis, "condition-cad", "Coronary artery disease"),
                Condition(basis, "condition-mdd", "Major depressive disorder")
            ],
            ServiceEvents = [],
            Requirements =
            [
                Requirement(basis, "requirement-310-a", "provision-310-a"),
                Requirement(basis, "requirement-310-b", "provision-310-b")
            ],
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

    private static ClaimIssueAdjudicationDetails Copy(
        ClaimIssueAdjudicationDetails source,
        IReadOnlyList<ServiceConnectionBasisClaimedConditionDetails>?
            claimedConditionBases = null,
        IReadOnlyList<ServiceConnectionBasisRequirementDetails>?
            requirements = null) =>
        new()
        {
            ClaimIssue = source.ClaimIssue,
            ClaimedConditions = source.ClaimedConditions,
            ClaimedConditionBases =
                claimedConditionBases ?? source.ClaimedConditionBases,
            ServiceConnectionTheories =
                source.ServiceConnectionTheories,
            ServiceConnectionBases = source.ServiceConnectionBases,
            ServiceConnectedConditions =
                source.ServiceConnectedConditions,
            PrescribedMedications = source.PrescribedMedications,
            Exposures = source.Exposures,
            PreexistingConditions = source.PreexistingConditions,
            Presumptions = source.Presumptions,
            MedicalOpinions = source.MedicalOpinions,
            BasisArtifacts = source.BasisArtifacts,
            ServiceEvents = source.ServiceEvents,
            Requirements = requirements ?? source.Requirements,
            Evidence = source.Evidence,
            Timeline = source.Timeline
        };

    private static ServiceConnectionBasis Basis(
        string id,
        ClaimIssueId claimIssueId) =>
        new()
        {
            Id = new ServiceConnectionBasisId(id),
            ClaimIssueId = claimIssueId,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-secondary")
        };

    private static ServiceConnectionBasisConditionDetails Condition(
        ServiceConnectionBasis basis,
        string id,
        string name) =>
        new()
        {
            Basis = basis,
            ServiceConnectedCondition =
                new MedicalCondition
                {
                    Id = new MedicalConditionId(id),
                    Name = name
                }
        };

    private static ServiceConnectionBasisRequirementDetails Requirement(
        ServiceConnectionBasis basis,
        string requirementId,
        string provisionId)
    {
        var id = new RequirementId(requirementId);

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis = basis,
            Requirement =
                new EMF.Extensions.VeteransClaims.Regulatory.Requirement
                {
                    Id = id,
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId(provisionId),
                    Description = "Secondary service connection requirement."
                },
            RegulatoryProvision = Provision(provisionId),
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = id,
                    Items = []
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = id,
                    Items = []
                }
        };
    }

    private static RegulatoryProvision Provision(string id) =>
        new()
        {
            Id = new RegulatoryProvisionId(id),
            RegulatoryAuthorityId =
                new RegulatoryAuthorityId("authority-38-cfr"),
            ProvisionType = RegulatoryProvisionTypes.Requirement,
            Citation = "38 CFR 3.310"
        };

    private static IntelligenceExecutionContext Context() =>
        new(
            "recognition-term-steward",
            new IntelligenceCorrelationId("proposal-coordinator-test"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

    private sealed class StubDetailsService(
        ClaimIssueAdjudicationDetails? details) :
        IClaimIssueAdjudicationDetailsService
    {
        public Task<ClaimIssueAdjudicationDetails?> GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(details);
    }

    private sealed class RecordingExecutor :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public List<TextStructuredExtractionRequest> Requests
        { get; } = [];

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(
                IntelligenceCapabilityIds.TextStructuredExtraction,
                capabilityId);

            Requests.Add(request);

            var requirementId =
                request.Text.Contains(
                    "Requirement ID: requirement-310-b",
                    StringComparison.Ordinal)
                    ? "requirement-310-b"
                    : "requirement-310-a";

            var output =
                "{\"terms\":[{" +
                $"\"requirementId\":\"{requirementId}\"," +
                "\"term\":\"obstructive sleep apnea\"," +
                "\"termType\":\"Phrase\"," +
                "\"recognitionRole\":\"Diagnosis\"," +
                "\"evidenceClassification\":\"MedicalEvidence\"," +
                "\"rationale\":\"Find claimed-condition evidence.\"}]}";

            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = output,
                    RequiresReview = true,
                    Metadata =
                        new IntelligenceExecutionMetadata
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

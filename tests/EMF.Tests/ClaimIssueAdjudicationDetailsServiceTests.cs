using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenIssueDoesNotExist()
    {

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new MissingClaimIssueRepository(),
                NeverCall<IConditionRepository>(),
                NeverCall<IServiceConnectionRepository>(),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var result =
            await service.GetAsync(
                new ClaimIssueId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_RejectsReturnedDifferentClaimIssue()
    {
        var requestedId =
            new ClaimIssueId("issue-requested");

        var returnedIssue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-other"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "service-connection"
            };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(returnedIssue),
                NeverCall<IConditionRepository>(),
                NeverCall<IServiceConnectionRepository>(),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(requestedId));

        Assert.Equal(
            "Claim issue lookup returned a different issue.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsClaimedConditionForDifferentClaimIssue()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var issue =
            new ClaimIssue
            {
                Id = issueId,
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "service-connection"
            };

        var condition =
            new ClaimedCondition
            {
                Id = new ClaimedConditionId("condition-1"),
                ClaimIssueId = new ClaimIssueId("issue-other"),
                Name = "Sleep apnea"
            };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ClaimedCondition>>(
                                [condition])
                        : throw new NotSupportedException()),
                NeverCall<IServiceConnectionRepository>(),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Claimed condition claim issue mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsTheoryForDifferentClaimIssue()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var issue =
            new ClaimIssue
            {
                Id = issueId,
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "service-connection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = new ClaimIssueId("issue-other"),
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name ==
                            "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                        : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service-connection theory claim issue mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsBasisForDifferentClaimIssue()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var issue =
            new ClaimIssue
            {
                Id = issueId,
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "service-connection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = issueId,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-1"),
                ClaimIssueId = new ClaimIssueId("issue-other"),
                ServiceConnectionTheoryId = theory.Id
            };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name ==
                            "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                        : m.Name ==
                            "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ServiceConnectionBasis>>(
                                    [basis])
                            : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service-connection basis claim issue mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsBasisForDifferentTheory()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var issue =
            new ClaimIssue
            {
                Id = issueId,
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "service-connection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = issueId,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-1"),
                ClaimIssueId = issueId,
                ServiceConnectionTheoryId =
                    new ServiceConnectionTheoryId("theory-other")
            };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name ==
                            "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                        : m.Name ==
                            "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ServiceConnectionBasis>>(
                                    [basis])
                            : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service-connection basis theory mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_ComposesAdjudicationDetails()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var condition = new ClaimedCondition
        {
            Id = new ClaimedConditionId("condition-001"),
            ClaimIssueId = issueId,
            Name = "Sleep apnea"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var serviceConnectedCondition = new MedicalCondition
        {
            Id = new MedicalConditionId("ptsd-001"),
            Name = "Posttraumatic stress disorder"
        };

        var serviceEvent = new ServiceEvent
        {
            Id = new ServiceEventId("service-event-001"),
            VeteranId = new VeteranId("veteran-001"),
            Description = "Documented duty event"
        };

        var requirement = new Requirement
        {
            Id = new RequirementId("requirement-001"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("provision-001"),
            Description =
                "Secondary service connection requirement"
        };

        var provision =
            new RegulatoryProvision
            {
                Id = requirement.RegulatoryProvisionId,
                RegulatoryAuthorityId =
                    new RegulatoryAuthorityId("authority-1"),
                ProvisionType = "Test",
                Citation = "38 CFR"
            };

        var presumptionProvision =
            new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("presumption-001"),
                RegulatoryAuthorityId =
                    new RegulatoryAuthorityId("authority-1"),
                ProvisionType = RegulatoryProvisionTypes.Presumption,
                Citation = "38 CFR 3.307"
            };

        var responsiveness =
            new RequirementEvidenceResponsivenessAssessment
            {
                RequirementId = requirement.Id,
                Items = []
            };

        var developmentChecklist =
            new EvidenceDevelopmentChecklist
            {
                RequirementId = requirement.Id,
                Items = []
            };

        var preexistingCondition =
            new MedicalCondition
            {
                Id = new MedicalConditionId("medical-condition-preexisting-1"),
                Name = "Preexisting condition"
            };

        var exposure =
            new Exposure
            {
                Id = new ExposureId("exposure-001"),
                VeteranId = new VeteranId("veteran-001"),
                ExposureType = "Hazardous material"
            };

        var evidence = new ClaimIssueEvidenceDetails
        {
            ClaimIssue = issue,
            Checklist = new ClaimIssueEvidenceChecklist
            {
                ClaimIssueId = issueId,
                RequirementChecklists = []
            },
            DevelopmentPlans = []
        };

        var timeline =
            new[]
            {
                new ClaimIssueAdjudicationEvent
                {
                    ClaimIssueId = issueId,
                    EventType =
                        ClaimIssueAdjudicationEventTypes.VaDecision,
                    OccurredAt =
                        new DateTimeOffset(
                            2026, 8, 1, 0, 0, 0,
                            TimeSpan.Zero),
                    Outcome = "Denied"
                }
            };

        var medicalConditionLookupCount = 0;
        var regulatoryProvisionLookupCount = 0;

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    method =>
                        method.Name == "GetClaimedConditionsAsync"
                            ? Task.FromResult<IReadOnlyList<ClaimedCondition>>(
                                [condition])
                            : method.Name == "GetMedicalConditionAsync"
                                ? Task.FromResult<MedicalCondition?>(
                                    medicalConditionLookupCount++ == 0
                                        ? serviceConnectedCondition
                                        : preexistingCondition)
                                : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    method =>
                        method.Name == "GetServiceConnectionTheoriesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>(
                                [theory])
                            : method.Name == "GetServiceConnectionBasesAsync"
                                ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>(
                                    [basis])
                                : method.Name == "GetServiceConnectedConditionIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<MedicalConditionId>>(
                                        [serviceConnectedCondition.Id])
                                    : method.Name == "GetPrescribedMedicationNamesAsync"
                                    ? Task.FromResult<IReadOnlyList<string>>(
                                        ["Pantoprazole"])
                                : method.Name == "GetPreexistingConditionIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<MedicalConditionId>>(
                                        [preexistingCondition.Id])
                                : method.Name == "GetPresumptionProvisionIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<RegulatoryProvisionId>>(
                                        [presumptionProvision.Id])
                                : method.Name == "GetExposureIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<ExposureId>>(
                                        [exposure.Id])
                                : method.Name == "GetServiceEventIdsAsync"
                                        ? Task.FromResult<IReadOnlyList<ServiceEventId>>(
                                            [serviceEvent.Id])
                                    : method.Name == "GetRequirementIdsAsync"
                                        ? Task.FromResult<IReadOnlyList<RequirementId>>(
                                            [requirement.Id])
                                        : throw new NotSupportedException()),
                Proxy<IServiceHistoryRepository>(
                    method =>
                        method.Name == "GetServiceEventAsync"
                            ? Task.FromResult<ServiceEvent?>(
                                serviceEvent)
                            : method.Name == "GetExposureAsync"
                                ? Task.FromResult<Exposure?>(exposure)
                            : throw new NotSupportedException()),
                Proxy<IRegulatoryRepository>(
                    method =>
                        method.Name == "GetRequirementAsync"
                            ? Task.FromResult<Requirement?>(
                                requirement)
                            : method.Name == "GetRegulatoryProvisionAsync"
                                ? Task.FromResult<RegulatoryProvision?>(
                                    regulatoryProvisionLookupCount++ == 0
                                        ? presumptionProvision
                                        : provision)
                                : throw new NotSupportedException()),
                Proxy<IRequirementEvidenceService>(
                    method =>
                        method.Name == "AssessResponsivenessAsync"
                            ? Task.FromResult(responsiveness)
                            : method.Name == "CreateChecklistAsync"
                                ? Task.FromResult(developmentChecklist)
                                : throw new NotSupportedException()),
                Proxy<IClaimIssueEvidenceDetailsService>(
                    method =>
                        method.Name == "GetAsync"
                            ? Task.FromResult<ClaimIssueEvidenceDetails?>(
                                evidence)
                            : throw new NotSupportedException()),
                Proxy<IClaimIssueAdjudicationTimelineService>(
                    method =>
                        method.Name == "GetAsync"
                            ? Task.FromResult<
                                IReadOnlyList<
                                    ClaimIssueAdjudicationEvent>>(
                                timeline)
                            : throw new NotSupportedException()));

        var result = await service.GetAsync(issueId);

        Assert.NotNull(result);
        Assert.Same(issue, result!.ClaimIssue);
        Assert.Same(condition, Assert.Single(result.ClaimedConditions));
        Assert.Same(theory, Assert.Single(result.ServiceConnectionTheories));
        Assert.Equal(
            ServiceConnectionTheoryTypes.Secondary,
            theory.TheoryType);
        Assert.Same(basis, Assert.Single(result.ServiceConnectionBases));
        Assert.Equal(
            theory.Id,
            basis.ServiceConnectionTheoryId);
        var resolved =
            Assert.Single(result.ServiceConnectedConditions);

        Assert.Same(basis, resolved.Basis);
        Assert.Same(
            serviceConnectedCondition,
            resolved.ServiceConnectedCondition);

        var resolvedMedication =
            Assert.Single(result.PrescribedMedications);

        Assert.Same(
            basis,
            resolvedMedication.Basis);

        Assert.Equal(
            "Pantoprazole",
            resolvedMedication.MedicationName);

        var resolvedExposure =
            Assert.Single(result.Exposures);

        Assert.Same(basis, resolvedExposure.Basis);
        Assert.Same(exposure, resolvedExposure.Exposure);

        var resolvedPreexistingCondition =
            Assert.Single(result.PreexistingConditions);

        Assert.Same(
            basis,
            resolvedPreexistingCondition.Basis);

        Assert.Same(
            preexistingCondition,
            resolvedPreexistingCondition.PreexistingCondition);

        var resolvedPresumption =
            Assert.Single(result.Presumptions);

        Assert.Same(
            basis,
            resolvedPresumption.Basis);

        Assert.Same(
            presumptionProvision,
            resolvedPresumption.PresumptionProvision);

        var resolvedServiceEvent =
            Assert.Single(result.ServiceEvents);

        Assert.Same(basis, resolvedServiceEvent.Basis);
        Assert.Same(
            serviceEvent,
            resolvedServiceEvent.ServiceEvent);

        var resolvedRequirement =
            Assert.Single(result.Requirements);

        Assert.Same(
            basis,
            resolvedRequirement.Basis);

        Assert.Same(
            requirement,
            resolvedRequirement.Requirement);

        Assert.Same(
            provision,
            resolvedRequirement.RegulatoryProvision);

        Assert.Same(
            responsiveness,
            resolvedRequirement.Responsiveness);

        Assert.Same(
            developmentChecklist,
            resolvedRequirement.DevelopmentChecklist);

        Assert.Same(evidence, result.Evidence);
        Assert.Same(timeline, result.Timeline);
    }

    [Fact]
    public async Task GetAsync_RejectsWrongServiceConnectedConditionIdentity()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requestedId = new MedicalConditionId("condition-001");

        var service = new ClaimIssueAdjudicationDetailsService(
            new FakeClaimIssueRepository(issue),
            Proxy<IConditionRepository>(
                m => m.Name == "GetClaimedConditionsAsync"
                    ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                    : m.Name == "GetMedicalConditionAsync"
                        ? Task.FromResult<MedicalCondition?>(
                            new MedicalCondition
                            {
                                Id = new MedicalConditionId("condition-other"),
                                Name = "Wrong condition"
                            })
                        : throw new NotSupportedException()),
            Proxy<IServiceConnectionRepository>(
                m => m.Name == "GetServiceConnectionTheoriesAsync"
                    ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                    : m.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                        : m.Name == "GetServiceConnectedConditionIdsAsync"
                            ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([requestedId])
                            : throw new NotSupportedException()),
            NeverCall<IServiceHistoryRepository>(),
            NeverCall<IRegulatoryRepository>(),
            NeverCall<IRequirementEvidenceService>(),
            NeverCall<IClaimIssueEvidenceDetailsService>(),
            NeverCall<IClaimIssueAdjudicationTimelineService>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(issueId));
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenLinkedServiceConnectedConditionCannotBeRead()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var conditionId =
            new MedicalConditionId("missing-condition");

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    method =>
                        method.Name == "GetClaimedConditionsAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ClaimedCondition>>([])
                            : method.Name == "GetMedicalConditionAsync"
                                ? Task.FromResult<MedicalCondition?>(null)
                                : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    method =>
                        method.Name == "GetServiceConnectionTheoriesAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ServiceConnectionTheory>>(
                                    [theory])
                            : method.Name == "GetServiceConnectionBasesAsync"
                                ? Task.FromResult<
                                    IReadOnlyList<ServiceConnectionBasis>>(
                                        [basis])
                                : method.Name ==
                                    "GetServiceConnectedConditionIdsAsync"
                                    ? Task.FromResult<
                                        IReadOnlyList<MedicalConditionId>>(
                                            [conditionId])
                                    : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service-connected condition could not be read.",
            exception.Message);
    }


    [Fact]
    public async Task GetAsync_RejectsWrongServiceEventIdentity()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Direct
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requestedId = new ServiceEventId("service-event-001");

        var service = new ClaimIssueAdjudicationDetailsService(
            new FakeClaimIssueRepository(issue),
            Proxy<IConditionRepository>(
                m => m.Name == "GetClaimedConditionsAsync"
                    ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                    : throw new NotSupportedException()),
            Proxy<IServiceConnectionRepository>(
                m => m.Name == "GetServiceConnectionTheoriesAsync"
                    ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                    : m.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                        : m.Name == "GetServiceConnectedConditionIdsAsync"
                            ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                            : m.Name == "GetServiceEventIdsAsync"
                                ? Task.FromResult<IReadOnlyList<ServiceEventId>>([requestedId])
                                : throw new NotSupportedException()),
            Proxy<IServiceHistoryRepository>(
                m => m.Name == "GetServiceEventAsync"
                    ? Task.FromResult<ServiceEvent?>(
                        new ServiceEvent
                        {
                            Id = new ServiceEventId("service-event-other"),
                            VeteranId = new VeteranId("veteran-001"),
                            Description = "Wrong event"
                        })
                    : throw new NotSupportedException()),
            NeverCall<IRegulatoryRepository>(),
            NeverCall<IRequirementEvidenceService>(),
            NeverCall<IClaimIssueEvidenceDetailsService>(),
            NeverCall<IClaimIssueAdjudicationTimelineService>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(issueId));
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenLinkedServiceEventCannotBeRead()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Direct
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var serviceEventId =
            new ServiceEventId("missing-service-event");

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    method =>
                        method.Name == "GetClaimedConditionsAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ClaimedCondition>>([])
                            : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    method =>
                        method.Name == "GetServiceConnectionTheoriesAsync"
                            ? Task.FromResult<
                                IReadOnlyList<ServiceConnectionTheory>>(
                                    [theory])
                            : method.Name == "GetServiceConnectionBasesAsync"
                                ? Task.FromResult<
                                    IReadOnlyList<ServiceConnectionBasis>>(
                                        [basis])
                                : method.Name ==
                                    "GetServiceConnectedConditionIdsAsync"
                                    ? Task.FromResult<
                                        IReadOnlyList<MedicalConditionId>>([])
                                    : method.Name == "GetServiceEventIdsAsync"
                                        ? Task.FromResult<
                                            IReadOnlyList<ServiceEventId>>(
                                                [serviceEventId])
                                        : throw new NotSupportedException()),
                Proxy<IServiceHistoryRepository>(
                    method =>
                        method.Name == "GetServiceEventAsync"
                            ? Task.FromResult<ServiceEvent?>(null)
                            : throw new NotSupportedException()),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service event could not be read.",
            exception.Message);
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenEvidenceDetailsCannotBeRead()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name == "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([])
                        : m.Name == "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([])
                            : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                NeverCall<IRegulatoryRepository>(),
                NeverCall<IRequirementEvidenceService>(),
                Proxy<IClaimIssueEvidenceDetailsService>(
                    m => m.Name == "GetAsync"
                        ? Task.FromResult<ClaimIssueEvidenceDetails?>(null)
                        : throw new NotSupportedException()),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Claim issue evidence details could not be read.",
            ex.Message);
    }


    [Fact]
    public async Task GetAsync_RejectsWrongRequirementIdentity()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requestedId = new RequirementId("requirement-001");

        var service = new ClaimIssueAdjudicationDetailsService(
            new FakeClaimIssueRepository(issue),
            Proxy<IConditionRepository>(
                m => m.Name == "GetClaimedConditionsAsync"
                    ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                    : throw new NotSupportedException()),
            Proxy<IServiceConnectionRepository>(
                m => m.Name == "GetServiceConnectionTheoriesAsync"
                    ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                    : m.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                        : m.Name == "GetServiceConnectedConditionIdsAsync"
                            ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                            : m.Name == "GetServiceEventIdsAsync"
                                ? Task.FromResult<IReadOnlyList<ServiceEventId>>([])
                                : m.Name == "GetRequirementIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<RequirementId>>([requestedId])
                                    : throw new NotSupportedException()),
            NeverCall<IServiceHistoryRepository>(),
            Proxy<IRegulatoryRepository>(
                m => m.Name == "GetRequirementAsync"
                    ? Task.FromResult<Requirement?>(
                        new Requirement
                        {
                            Id = new RequirementId("requirement-other"),
                            RegulatoryProvisionId =
                                new RegulatoryProvisionId("provision-001"),
                            Description = "Wrong requirement"
                        })
                    : throw new NotSupportedException()),
            NeverCall<IRequirementEvidenceService>(),
            NeverCall<IClaimIssueEvidenceDetailsService>(),
            NeverCall<IClaimIssueAdjudicationTimelineService>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(issueId));
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenLinkedRequirementCannotBeRead()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requirementId =
            new RequirementId("missing-requirement");

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name == "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                        : m.Name == "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                            : m.Name == "GetServiceConnectedConditionIdsAsync"
                                ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                                : m.Name == "GetServiceEventIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<ServiceEventId>>([])
                                    : m.Name == "GetRequirementIdsAsync"
                                        ? Task.FromResult<IReadOnlyList<RequirementId>>([requirementId])
                                        : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                Proxy<IRegulatoryRepository>(
                    m => m.Name == "GetRequirementAsync"
                        ? Task.FromResult<Requirement?>(null)
                        : throw new NotSupportedException()),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Service-connection requirement could not be read.",
            ex.Message);
    }


    [Fact]
    public async Task GetAsync_ThrowsWhenRegulatoryProvisionCannotBeRead()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requirement = new Requirement
        {
            Id = new RequirementId("requirement-001"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("missing-provision"),
            Description = "Requirement"
        };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name == "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                        : m.Name == "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                            : m.Name == "GetServiceConnectedConditionIdsAsync"
                                ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                                : m.Name == "GetServiceEventIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<ServiceEventId>>([])
                                    : m.Name == "GetRequirementIdsAsync"
                                        ? Task.FromResult<IReadOnlyList<RequirementId>>([requirement.Id])
                                        : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                Proxy<IRegulatoryRepository>(
                    m => m.Name == "GetRequirementAsync"
                        ? Task.FromResult<Requirement?>(requirement)
                        : m.Name == "GetRegulatoryProvisionAsync"
                            ? Task.FromResult<RegulatoryProvision?>(null)
                            : throw new NotSupportedException()),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Regulatory provision could not be read.",
            ex.Message);
    }


    [Fact]
    public async Task GetAsync_RejectsWrongRegulatoryProvisionIdentity()
    {
        var issueId = new ClaimIssueId("issue-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-001"),
            ClaimIssueType = "service-connection"
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var requirement = new Requirement
        {
            Id = new RequirementId("requirement-001"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("missing-provision"),
            Description = "Requirement"
        };

        var service =
            new ClaimIssueAdjudicationDetailsService(
                new FakeClaimIssueRepository(issue),
                Proxy<IConditionRepository>(
                    m => m.Name == "GetClaimedConditionsAsync"
                        ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                        : throw new NotSupportedException()),
                Proxy<IServiceConnectionRepository>(
                    m => m.Name == "GetServiceConnectionTheoriesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                        : m.Name == "GetServiceConnectionBasesAsync"
                            ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                            : m.Name == "GetServiceConnectedConditionIdsAsync"
                                ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                                : m.Name == "GetServiceEventIdsAsync"
                                    ? Task.FromResult<IReadOnlyList<ServiceEventId>>([])
                                    : m.Name == "GetRequirementIdsAsync"
                                        ? Task.FromResult<IReadOnlyList<RequirementId>>([requirement.Id])
                                        : throw new NotSupportedException()),
                NeverCall<IServiceHistoryRepository>(),
                Proxy<IRegulatoryRepository>(
                    m => m.Name == "GetRequirementAsync"
                        ? Task.FromResult<Requirement?>(requirement)
                        : m.Name == "GetRegulatoryProvisionAsync"
                            ? Task.FromResult<RegulatoryProvision?>(
                                new RegulatoryProvision
                                {
                                    Id = new RegulatoryProvisionId("provision-other"),
                                    RegulatoryAuthorityId =
                                        new RegulatoryAuthorityId("authority-1"),
                                    ProvisionType = "Test",
                                    Citation = "38 CFR"
                                })
                            : throw new NotSupportedException()),
                NeverCall<IRequirementEvidenceService>(),
                NeverCall<IClaimIssueEvidenceDetailsService>(),
                NeverCall<IClaimIssueAdjudicationTimelineService>());

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(issueId));

        Assert.Equal(
            "Regulatory provision identity mismatch.",
            ex.Message);
    }


    [Fact]
    public async Task GetAsync_IncludesBasisMedicalOpinionAndRole()
    {
        var issueId = new ClaimIssueId("issue-medop-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = new ClaimId("claim-medop-001"),
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        var theory = new ServiceConnectionTheory
        {
            Id = new ServiceConnectionTheoryId("theory-medop-001"),
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-medop-001"),
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theory.Id
        };

        var opinion = new MedicalOpinion
        {
            Id = new MedicalOpinionId("opinion-medop-001"),
            ClaimIssueId = issueId,
            Question = "Related to service?",
            Opinion = "At least as likely as not."
        };

        var association = new ServiceConnectionBasisMedicalOpinion
        {
            ServiceConnectionBasisId = basis.Id,
            MedicalOpinionId = opinion.Id,
            Role = ServiceConnectionBasisTraceabilityRoles.Supporting
        };

        var evidence = new ClaimIssueEvidenceDetails
        {
            ClaimIssue = issue,
            Checklist = null!,
            DevelopmentPlans = []
        };

        var service = new ClaimIssueAdjudicationDetailsService(
            new FakeClaimIssueRepository(issue),
            Proxy<IConditionRepository>(
                m => m.Name == "GetClaimedConditionsAsync"
                    ? Task.FromResult<IReadOnlyList<ClaimedCondition>>([])
                    : throw new NotSupportedException()),
            Proxy<IServiceConnectionRepository>(
                m => m.Name == "GetServiceConnectionTheoriesAsync"
                    ? Task.FromResult<IReadOnlyList<ServiceConnectionTheory>>([theory])
                    : m.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                    : m.Name == "GetServiceConnectedConditionIdsAsync"
                        ? Task.FromResult<IReadOnlyList<MedicalConditionId>>([])
                    : m.Name == "GetBasisMedicalOpinionsAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceConnectionBasisMedicalOpinion>>(
                            [association])
                    : m.Name == "GetServiceEventIdsAsync"
                        ? Task.FromResult<IReadOnlyList<ServiceEventId>>([])
                    : m.Name == "GetRequirementIdsAsync"
                        ? Task.FromResult<IReadOnlyList<RequirementId>>([])
                    : throw new NotSupportedException()),
            NeverCall<IServiceHistoryRepository>(),
            NeverCall<IRegulatoryRepository>(),
            NeverCall<IRequirementEvidenceService>(),
            Proxy<IClaimIssueEvidenceDetailsService>(
                m => m.Name == "GetAsync"
                    ? Task.FromResult<ClaimIssueEvidenceDetails?>(evidence)
                    : throw new NotSupportedException()),
            Proxy<IClaimIssueAdjudicationTimelineService>(
                m => m.Name == "GetAsync"
                    ? Task.FromResult<IReadOnlyList<ClaimIssueAdjudicationEvent>>([])
                    : throw new NotSupportedException()),
            Proxy<IMedicalOpinionRepository>(
                m => m.Name == "GetMedicalOpinionAsync"
                    ? Task.FromResult<MedicalOpinion?>(opinion)
                    : throw new NotSupportedException()));

        var result = await service.GetAsync(issueId);

        Assert.NotNull(result);

        var details = Assert.Single(result!.MedicalOpinions);

        Assert.Equal(basis.Id, details.Basis.Id);
        Assert.Equal(opinion.Id, details.MedicalOpinion.Id);
        Assert.Equal(
            ServiceConnectionBasisTraceabilityRoles.Supporting,
            details.Role);
    }


    private sealed class MissingClaimIssueRepository :
        FakeClaimIssueRepository
    {
        public MissingClaimIssueRepository() : base(null) { }
    }

    private class FakeClaimIssueRepository :
        IClaimIssueRepository
    {
        private readonly ClaimIssue? _issue;

        public FakeClaimIssueRepository(ClaimIssue? issue) =>
            _issue = issue;

        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_issue);

        public Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static T NeverCall<T>()
        where T : class =>
        Proxy<T>(
            method => throw new InvalidOperationException(
                $"{method.Name} should not have been called."));

    private static T Proxy<T>(
        Func<MethodInfo, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?>? Handler { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            try
            {
                return Handler!(targetMethod!);
            }
            catch (NotSupportedException)
                when (targetMethod!.Name ==
                    "GetPrescribedMedicationNamesAsync")
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }
            catch (NotSupportedException)
                when (targetMethod!.Name == "GetExposureIdsAsync")
            {
                return Task.FromResult<IReadOnlyList<ExposureId>>([]);
            }
            catch (NotSupportedException)
                when (targetMethod!.Name == "GetPreexistingConditionIdsAsync")
            {
                return Task.FromResult<IReadOnlyList<MedicalConditionId>>([]);
            }
            catch (NotSupportedException)
                when (targetMethod!.Name == "GetPresumptionProvisionIdsAsync")
            {
                return Task.FromResult<IReadOnlyList<RegulatoryProvisionId>>([]);
            }
        }
    }

}

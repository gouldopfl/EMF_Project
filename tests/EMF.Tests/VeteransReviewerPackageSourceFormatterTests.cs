using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageSourceFormatterTests
{
    [Fact]
    public void Format_IncludesClaimIssueAndClaimedConditions()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-reviewer-1"),
                ClaimId = new ClaimId("claim-reviewer-1"),
                ClaimIssueType = "ServiceConnection"
            };

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions =
                [
                    new ClaimedCondition
                    {
                        Id =
                            new ClaimedConditionId(
                                "condition-reviewer-1"),
                        ClaimIssueId = issue.Id,
                        Name = "Sleep apnea"
                    }
                ],
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

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                details);

        Assert.Contains(
            "Claim Issue: issue-reviewer-1",
            text);

        Assert.Contains(
            "Claim Type: ServiceConnection",
            text);

        Assert.Contains(
            "Claimed Conditions:",
            text);

        Assert.Contains(
            "- condition-reviewer-1: Sleep apnea",
            text);
    }

    [Fact]
    public void Format_IncludesServiceConnectionTheoriesAndBases()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-reviewer-2"),
                ClaimId = new ClaimId("claim-reviewer-2"),
                ClaimIssueType = "ServiceConnection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-reviewer-1"),
                ClaimIssueId = issue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-reviewer-1"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ServiceConnectionTheories = [theory],
                ServiceConnectionBases = [basis],
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

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                details);

        Assert.Contains(
            "Service-Connection Theories:",
            text);

        Assert.Contains(
            "- theory-reviewer-1: Secondary",
            text);

        Assert.Contains(
            "Service-Connection Bases:",
            text);

        Assert.Contains(
            "- basis-reviewer-1: theory theory-reviewer-1",
            text);
    }


    [Fact]
    public void Format_IncludesBasisConditionsAndServiceEvents()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-reviewer-3"),
                ClaimId = new ClaimId("claim-reviewer-3"),
                ClaimIssueType = "ServiceConnection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-reviewer-3"),
                ClaimIssueId = issue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-reviewer-3"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

        var serviceConnectedCondition =
            new MedicalCondition
            {
                Id =
                    new MedicalConditionId(
                        "medical-condition-reviewer-1"),
                Name = "Service-connected condition"
            };

        var serviceEvent =
            new ServiceEvent
            {
                Id =
                    new ServiceEventId(
                        "service-event-reviewer-1"),
                VeteranId =
                    new VeteranId("veteran-reviewer-1"),
                Description = "Documented duty event"
            };

        var details =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = issue,
                ClaimedConditions = [],
                ServiceConnectionTheories = [theory],
                ServiceConnectionBases = [basis],
                ServiceConnectedConditions =
                [
                    new ServiceConnectionBasisConditionDetails
                    {
                        Basis = basis,
                        ServiceConnectedCondition =
                            serviceConnectedCondition
                    }
                ],
                ServiceEvents =
                [
                    new ServiceConnectionBasisServiceEventDetails
                    {
                        Basis = basis,
                        ServiceEvent = serviceEvent
                    }
                ],
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

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                details);

        Assert.Contains(
            "Service-Connected Conditions:",
            text);

        Assert.Contains(
            "- basis basis-reviewer-3: " +
            "medical-condition-reviewer-1: " +
            "Service-connected condition",
            text);

        Assert.Contains(
            "Service Events:",
            text);

        Assert.Contains(
            "- basis basis-reviewer-3: " +
            "service-event-reviewer-1: " +
            "Documented duty event",
            text);
    }


    private static ClaimIssueAdjudicationDetails CreateDetails(
        ClaimIssue issue,
        IReadOnlyList<ServiceConnectionBasisRequirementDetails>
            requirements,
        ClaimIssueEvidenceDetails? evidence = null,
        IReadOnlyList<ClaimIssueAdjudicationEvent>? timeline = null,
        IReadOnlyList<ServiceConnectionBasisMedicationDetails>?
            prescribedMedications = null)
    {
        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [],
            ServiceConnectionBases = [],
            ServiceConnectedConditions = [],
            PrescribedMedications =
                prescribedMedications ?? [],
            ServiceEvents = [],
            Requirements = requirements,
            Evidence =
                evidence ??
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
            Timeline = timeline ?? []
        };
    }



    [Fact]
    public void Format_IncludesBasisExposures()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-exposure-1"),
            ClaimId = new ClaimId("claim-exposure-1"),
            ClaimIssueType = "ServiceConnection"
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-exposure-1"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-exposure-1")
        };

        var exposure = new Exposure
        {
            Id = new ExposureId("exposure-reviewer-1"),
            VeteranId = new VeteranId("veteran-reviewer-1"),
            ExposureType = "Hazardous material"
        };

        var details = CreateDetails(issue, []);
        details = new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = details.ClaimIssue,
            ClaimedConditions = details.ClaimedConditions,
            ServiceConnectionTheories = details.ServiceConnectionTheories,
            ServiceConnectionBases = details.ServiceConnectionBases,
            ServiceConnectedConditions = details.ServiceConnectedConditions,
            PrescribedMedications = details.PrescribedMedications,
            Exposures =
            [
                new ServiceConnectionBasisExposureDetails
                {
                    Basis = basis,
                    Exposure = exposure
                }
            ],
            ServiceEvents = details.ServiceEvents,
            Requirements = details.Requirements,
            Evidence = details.Evidence,
            Timeline = details.Timeline
        };

        var text =
            VeteransReviewerPackageSourceFormatter.Format(details);

        Assert.Contains("Exposures:", text);
        Assert.Contains(
            "- basis basis-exposure-1: exposure-reviewer-1: Hazardous material",
            text);
    }


    [Fact]
    public void Format_IncludesPreexistingConditions()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-preexisting-1"),
            ClaimId = new ClaimId("claim-preexisting-1"),
            ClaimIssueType = "ServiceConnection"
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-preexisting-1"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-preexisting-1")
        };

        var condition = new MedicalCondition
        {
            Id = new MedicalConditionId("medical-preexisting-1"),
            Name = "Preexisting condition"
        };

        var details = CreateDetails(issue, []);

        details = new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = details.ClaimIssue,
            ClaimedConditions = details.ClaimedConditions,
            ServiceConnectionTheories = details.ServiceConnectionTheories,
            ServiceConnectionBases = details.ServiceConnectionBases,
            ServiceConnectedConditions = details.ServiceConnectedConditions,
            PrescribedMedications = details.PrescribedMedications,
            Exposures = details.Exposures,
            PreexistingConditions =
            [
                new ServiceConnectionBasisPreexistingConditionDetails
                {
                    Basis = basis,
                    PreexistingCondition = condition
                }
            ],
            ServiceEvents = details.ServiceEvents,
            Requirements = details.Requirements,
            Evidence = details.Evidence,
            Timeline = details.Timeline
        };

        var text =
            VeteransReviewerPackageSourceFormatter.Format(details);

        Assert.Contains("Preexisting Conditions:", text);
        Assert.Contains(
            "- basis basis-preexisting-1: medical-preexisting-1: Preexisting condition",
            text);
    }

    [Fact]
    public void Format_IncludesRequirementAndRegulation()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-4"),
            ClaimId = new ClaimId("claim-reviewer-4"),
            ClaimIssueType = "ServiceConnection"
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-reviewer-4"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-reviewer-4")
        };

        var requirement = new Requirement
        {
            Id = new RequirementId("requirement-reviewer-1"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("provision-reviewer-1"),
            Description =
                "Secondary service connection requirement"
        };

        var details = CreateDetails(
            issue,
            [
                new ServiceConnectionBasisRequirementDetails
                {
                    Basis = basis,
                    Requirement = requirement,
                    RegulatoryProvision =
                        new RegulatoryProvision
                        {
                            Id = requirement.RegulatoryProvisionId,
                            RegulatoryAuthorityId =
                                new RegulatoryAuthorityId("authority-1"),
                            ProvisionType = "Regulation",
                            Citation = "38 CFR 3.310"
                        },
                    Responsiveness =
                        new RequirementEvidenceResponsivenessAssessment
                        {
                            RequirementId = requirement.Id,
                            Items = []
                        },
                    DevelopmentChecklist =
                        new EvidenceDevelopmentChecklist
                        {
                            RequirementId = requirement.Id,
                            Items = []
                        }
                }
            ]);

        var text =
            VeteransReviewerPackageSourceFormatter.Format(details);

        Assert.Contains("Requirements:", text);
        Assert.Contains(
            "- basis basis-reviewer-4: requirement-reviewer-1: " +
            "Secondary service connection requirement",
            text);
        Assert.Contains("  Regulation: 38 CFR 3.310", text);
        Assert.Contains(
            "  Evidence Responsiveness: 0 matching, 0 missing",
            text);
    }


    private static ServiceConnectionBasisRequirementDetails
        CreateRequirementDetails(
            ClaimIssue issue,
            IReadOnlyList<RequirementEvidenceResponsivenessItem>
                responsivenessItems,
            IReadOnlyList<EvidenceDevelopmentChecklistItem>
                checklistItems)
    {
        var requirementId =
            new RequirementId("requirement-reviewer-helper");

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis =
                new ServiceConnectionBasis
                {
                    Id = new ServiceConnectionBasisId("basis-helper"),
                    ClaimIssueId = issue.Id,
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId("theory-helper")
                },
            Requirement =
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId("provision-helper"),
                    Description = "Test requirement"
                },
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id = new RegulatoryProvisionId("provision-helper"),
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId("authority-helper"),
                    ProvisionType = "Regulation",
                    Citation = "38 CFR"
                },
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirementId,
                    Items = responsivenessItems
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirementId,
                    Items = checklistItems
                }
        };
    }


    [Fact]
    public void Format_IncludesOutstandingEvidence()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-5"),
            ClaimId = new ClaimId("claim-reviewer-5"),
            ClaimIssueType = "ServiceConnection"
        };

        var requirementId =
            new RequirementId("requirement-reviewer-helper");

        var guidance = new EvidenceRequirementGuidance
        {
            Id =
                new EvidenceRequirementGuidanceId(
                    "guidance-reviewer-1"),
            RequirementId = requirementId,
            EvidenceClassification =
                EvidenceClassifications.MedicalOpinion,
            GuidanceRole =
                EvidenceGuidanceRoles.SupportsRequirement,
            Description = "Medical nexus opinion"
        };

        var details = CreateDetails(
            issue,
            [
                CreateRequirementDetails(
                    issue,
                    [
                        new RequirementEvidenceResponsivenessItem
                        {
                            Guidance = guidance,
                            HasMatchingEvidence = false
                        }
                    ],
                    [
                        new EvidenceDevelopmentChecklistItem
                        {
                            RequirementId = requirementId,
                            EvidenceClassification =
                                guidance.EvidenceClassification,
                            GuidanceRole = guidance.GuidanceRole,
                            Description = guidance.Description
                        }
                    ])
            ]);

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                details);

        Assert.Contains("  Outstanding Evidence:", text);

        Assert.Contains(
            "  - MedicalOpinion / SupportsRequirement: " +
            "Medical nexus opinion",
            text);
    }


    [Fact]
    public void Format_IncludesEvidenceChecklistAndPlans()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-6"),
            ClaimId = new ClaimId("claim-reviewer-6"),
            ClaimIssueType = "ServiceConnection"
        };

        var requirementId =
            new RequirementId("requirement-reviewer-6");

        var evidence = new ClaimIssueEvidenceDetails
        {
            ClaimIssue = issue,
            Checklist =
                new ClaimIssueEvidenceChecklist
                {
                    ClaimIssueId = issue.Id,
                    RequirementChecklists =
                    [
                        new EvidenceDevelopmentChecklist
                        {
                            RequirementId = requirementId,
                            Items =
                            [
                                new EvidenceDevelopmentChecklistItem
                                {
                                    RequirementId = requirementId,
                                    EvidenceClassification =
                                        EvidenceClassifications.MedicalOpinion,
                                    GuidanceRole =
                                        EvidenceGuidanceRoles.SupportsRequirement,
                                    Description = "Missing nexus evidence"
                                }
                            ]
                        }
                    ]
                },
            DevelopmentPlans =
            [
                new EvidenceDevelopmentPlan
                {
                    Id =
                        new EvidenceDevelopmentPlanId(
                            "plan-reviewer-1"),
                    ClaimIssueId = issue.Id,
                    Description = "Develop missing evidence"
                }
            ]
        };

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                CreateDetails(issue, [], evidence));

        Assert.Contains("Claim-Issue Evidence:", text);
        Assert.Contains("  Evidence Checklist:", text);
        Assert.Contains(
            "  - requirement requirement-reviewer-6",
            text);
        Assert.Contains(
            "    - MedicalOpinion / SupportsRequirement: " +
            "Missing nexus evidence",
            text);
        Assert.Contains("  Development Plans:", text);
        Assert.Contains(
            "  - plan-reviewer-1: Develop missing evidence",
            text);
    }


    [Fact]
    public void Format_IncludesAdjudicationTimeline()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-7"),
            ClaimId = new ClaimId("claim-reviewer-7"),
            ClaimIssueType = "ServiceConnection"
        };

        var occurredAt =
            new DateTimeOffset(
                2026, 8, 1, 0, 0, 0,
                TimeSpan.Zero);

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                CreateDetails(
                    issue,
                    [],
                    timeline:
                    [
                        new ClaimIssueAdjudicationEvent
                        {
                            ClaimIssueId = issue.Id,
                            EventType =
                                ClaimIssueAdjudicationEventTypes.VaDecision,
                            OccurredAt = occurredAt,
                            ReferenceId = "decision-reviewer-1",
                            Outcome = "Denied",
                            Description = "Claim denied"
                        }
                    ]));

        Assert.Contains("Timeline:", text);

        Assert.Contains(
            "- 2026-08-01T00:00:00.0000000+00:00 | " +
            "VaDecision | reference decision-reviewer-1 | " +
            "outcome Denied: Claim denied",
            text);
    }


    [Fact]
    public void Format_IncludesEvidenceSourceTextAndLineage()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-evidence"),
            ClaimId = new ClaimId("claim-reviewer-evidence"),
            ClaimIssueType = "ServiceConnection"
        };

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                CreateDetails(issue, []),
                [
                    new VeteransReviewerEvidenceSource
                    {
                        ArtifactId =
                            new ArtifactId("artifact-blue-button"),
                        Classifications =
                            [EvidenceClassifications.MedicalEvidence],
                        Text =
                            "Pantoprazole appears in the medication record."
                    }
                ]);

        Assert.Contains(
            "Evidence of Record (untrusted source text):",
            text);

        Assert.Contains(
            "- Artifact artifact-blue-button",
            text);

        Assert.Contains(
            "Classifications: MedicalEvidence",
            text);

        Assert.Contains(
            "Pantoprazole appears in the medication record.",
            text);

        Assert.Contains(
            "--- BEGIN EVIDENCE TEXT ---",
            text);

        Assert.Contains(
            "--- END EVIDENCE TEXT ---",
            text);
    }



    [Fact]
    public void Format_IncludesPrescribedMedicationBasis()
    {
        var issue = new ClaimIssue
        {
            Id = new ClaimIssueId("issue-reviewer-medication"),
            ClaimId = new ClaimId("claim-reviewer-medication"),
            ClaimIssueType = "ServiceConnection"
        };

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId(
                "basis-reviewer-medication"),
            ClaimIssueId = issue.Id,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId(
                    "theory-reviewer-medication")
        };

        var text =
            VeteransReviewerPackageSourceFormatter.Format(
                CreateDetails(
                    issue,
                    [],
                    prescribedMedications:
                    [
                        new ServiceConnectionBasisMedicationDetails
                        {
                            Basis = basis,
                            MedicationName = "Pantoprazole"
                        }
                    ]));

        Assert.Contains(
            "Prescribed Medications:",
            text);

        Assert.Contains(
            "- basis basis-reviewer-medication: Pantoprazole",
            text);
    }


}

using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Orchestration.Models;

namespace EMF.Tests;

public sealed class DevelopEvidenceGapWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_SucceedsWhenGapExists()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-1"),
            ClaimIssueId = new ClaimIssueId("issue-1"),
            RequirementId = new RequirementId("req-1"),
            Description = "Missing evidence."
        };

        var development = new FakeDevelopmentRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                development,
                gap.Id);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.True(result.Succeeded);
        Assert.NotNull(development.Result);
        Assert.Equal(gap.Id, development.Result!.EvidenceGapId);
        Assert.Equal(gap.RequirementId, development.Result.RequirementId);
    }


    [Fact]
    public async Task ExecuteAsync_LoadsGuidanceForGapRequirement()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-guidance-1"),
            ClaimIssueId = new ClaimIssueId("issue-guidance-1"),
            RequirementId = new RequirementId("req-guidance-1"),
            Description = "Missing evidence."
        };

        var guidance = new FakeGuidanceRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                guidance,
                new FakeDevelopmentRepository(),
                gap.Id);

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-guidance-1")
            });

        Assert.Equal(
            gap.RequirementId,
            guidance.RequestedRequirementId);
    }


    [Fact]
    public async Task ExecuteAsync_PropagatesResultPersistenceFailure()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-fail-1"),
            ClaimIssueId = new ClaimIssueId("issue-fail-1"),
            RequirementId = new RequirementId("req-fail-1"),
            Description = "Missing evidence."
        };

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                new FailingDevelopmentRepository(),
                gap.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-fail-1")
                }));
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenGapIsMissing()
    {
        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(null),
                new FakeGuidanceRepository(),
                new FakeDevelopmentRepository(),
                new EvidenceGapId("gap-1"));

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-1")
                });

        Assert.False(result.Succeeded);
    }



    [Fact]
    public async Task ExecuteAsync_PersistsRecognitionMatches()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-recognition-1"),
            ClaimIssueId = new ClaimIssueId("issue-recognition-1"),
            RequirementId = new RequirementId("req-recognition-1"),
            Description = "Missing evidence."
        };

        var recognition =
            new EvidenceRecognitionMatch
            {
                TermId =
                    new EvidenceRecognitionTermId("term-1"),
                Term = "instability",
                RecognitionRole =
                    EvidenceRecognitionRoles.SeverityCriterion,
                AuthoritySource = "38 CFR"
            };

        var development =
            new FakeDevelopmentRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                development,
                new FakeRecognitionCoordinator(recognition),
                gap.Id);

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId =
                    new WorkflowId("workflow-recognition-1")
            });

        var stored =
            Assert.Single(
                development.Result!.RecognitionMatches);

        Assert.Equal(recognition.TermId, stored.TermId);
        Assert.Equal(recognition.Term, stored.Term);
    }




    [Fact]
    public async Task ExecuteAsync_ClassifiesMatchedArtifact()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-classify-1"),
            ClaimIssueId = new ClaimIssueId("issue-classify-1"),
            RequirementId = new RequirementId("req-classify-1"),
            Description = "Missing evidence."
        };

        var termId = new EvidenceRecognitionTermId("term-classify-1");
        var artifactId = new ArtifactId("artifact-classify-1");

        var classifier = new FakeClassificationService();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                new FakeRepository(gap),
                new FakeGuidanceRepository(),
                new FakeDevelopmentRepository(),
                new FakeRecognitionCoordinator(
                    new EvidenceRecognitionMatch
                    {
                        TermId = termId,
                        Term = "medical opinion",
                        RecognitionRole =
                            EvidenceRecognitionRoles.EvidenceType,
                        EvidenceClassification =
                            EvidenceClassifications.MedicalOpinion,
                        AuthoritySource = "38 CFR"
                    },
                    new EvidenceRecognitionMatchArtifact
                    {
                        RecognitionTermId = termId,
                        ArtifactId = artifactId,
                        Role = "primary"
                    }),
                classifier,
                gap.Id);

        await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-classify-1")
            });

        Assert.Equal(artifactId, classifier.ArtifactId);
        Assert.Equal(
            EvidenceClassifications.MedicalOpinion,
            classifier.Classification);
        Assert.Equal(gap.ClaimIssueId, classifier.ClaimIssueId);
        Assert.Equal(
            gap.RequirementId,
            classifier.RequirementId);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsRequirementEvidencePresence()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-assessment-1"),
            ClaimIssueId = new ClaimIssueId("issue-assessment-1"),
            RequirementId = new RequirementId("req-assessment-1"),
            Description = "Missing evidence."
        };

        var evidenceService =
            new FakeRequirementEvidenceService(true);

        var repository =
            new FakeRepository(gap);

        var developmentRepository =
            new FakeDevelopmentRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                repository,
                new FakeGuidanceRepository(),
                developmentRepository,
                new FakeRecognitionCoordinator(),
                null,
                evidenceService,
                gap.Id);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-assessment-1")
                });

        Assert.Equal(
            gap.RequirementId,
            evidenceService.RequirementId);

        Assert.Contains(
            "evidence present: True",
            result.Message);

        Assert.Contains(
            "matching guidance items: 1",
            result.Message);

        Assert.Contains(
            "missing guidance items: 0",
            result.Message);

        Assert.Equal(
            EvidenceGapStatuses.Resolved,
            repository.UpdatedStatus);

        Assert.NotNull(developmentRepository.Result);

        Assert.Equal(
            1,
            developmentRepository.Result!.MatchingGuidanceItemCount);

        Assert.Equal(
            0,
            developmentRepository.Result.MissingGuidanceItemCount);

        Assert.Equal(
            EvidenceGapStatuses.Resolved,
            developmentRepository.Result.ResultingGapStatus);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesGapOpenWhenEvidenceIsStillMissing()
    {
        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-missing-1"),
            ClaimIssueId = new ClaimIssueId("issue-missing-1"),
            RequirementId = new RequirementId("req-missing-1"),
            Description = "Missing evidence."
        };

        var repository =
            new FakeRepository(gap);

        var developmentRepository =
            new FakeDevelopmentRepository();

        var activity =
            new DevelopEvidenceGapWorkflowActivity(
                repository,
                new FakeGuidanceRepository(),
                developmentRepository,
                new FakeRecognitionCoordinator(),
                null,
                new FakeRequirementEvidenceService(false),
                gap.Id);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-missing-1")
                });

        Assert.True(result.Succeeded);

        Assert.Contains(
            "missing guidance items: 1",
            result.Message);

        Assert.Null(repository.UpdatedStatus);
    }

    private sealed class FakeRequirementEvidenceService :
        IRequirementEvidenceService
    {
        private readonly bool _hasEvidence;

        public FakeRequirementEvidenceService(bool hasEvidence)
        {
            _hasEvidence = hasEvidence;
        }

        public RequirementId? RequirementId { get; private set; }

        public Task<IReadOnlyList<EvidenceClassification>>
            GetEvidenceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RequirementEvidenceAssessment>
            AssessAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            RequirementId = requirementId;

            return Task.FromResult(
                new RequirementEvidenceAssessment
                {
                    RequirementId = requirementId,
                    Evidence =
                        _hasEvidence
                            ? new[]
                            {
                                new EvidenceClassification
                                {
                                    Id =
                                        new EvidenceClassificationId(
                                            "classification-assessment-1"),
                                    ArtifactId =
                                        new ArtifactId(
                                            "artifact-assessment-1"),
                                    Classification =
                                        EvidenceClassifications.MedicalEvidence
                                }
                            }
                            : Array.Empty<EvidenceClassification>()
                });
        }


        public Task<RequirementEvidenceResponsivenessAssessment>
            AssessResponsivenessAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            RequirementId = requirementId;

            return Task.FromResult(
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirementId,
                    Items =
                        _hasEvidence
                            ? new[]
                            {
                                new RequirementEvidenceResponsivenessItem
                                {
                                    Guidance =
                                        new EvidenceRequirementGuidance
                                        {
                                            Id =
                                                new EvidenceRequirementGuidanceId(
                                                    "guidance-assessment-1"),
                                            RequirementId = requirementId,
                                            EvidenceClassification =
                                                EvidenceClassifications.MedicalEvidence,
                                            GuidanceRole =
                                                EvidenceGuidanceRoles.SupportsRequirement,
                                            Description =
                                                "Matching evidence guidance."
                                        },
                                    HasMatchingEvidence = true
                                }
                            }
                            : new[]
                            {
                                new RequirementEvidenceResponsivenessItem
                                {
                                    Guidance =
                                        new EvidenceRequirementGuidance
                                        {
                                            Id =
                                                new EvidenceRequirementGuidanceId(
                                                    "guidance-missing-1"),
                                            RequirementId = requirementId,
                                            EvidenceClassification =
                                                EvidenceClassifications.MedicalEvidence,
                                            GuidanceRole =
                                                EvidenceGuidanceRoles.SupportsRequirement,
                                            Description =
                                                "Missing evidence guidance."
                                        },
                                    HasMatchingEvidence = false
                                }
                            }
                });
        }

        public Task<EvidenceDevelopmentChecklist>
            CreateChecklistAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

    }

    private sealed class FakeRecognitionCoordinator :
        IEvidenceRecognitionCoordinator
    {
        private readonly
            IReadOnlyList<EvidenceRecognitionMatch> _matches;
        private readonly
            IReadOnlyList<EvidenceRecognitionMatchArtifact> _links;

        public FakeRecognitionCoordinator(
            params EvidenceRecognitionMatch[] matches)
        {
            _matches = matches;
            _links = [];
        }

        public FakeRecognitionCoordinator(
            EvidenceRecognitionMatch match,
            EvidenceRecognitionMatchArtifact link)
        {
            _matches = [match];
            _links = [link];
        }

        public Task<EvidenceRecognitionResult>
            RecognizeAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new EvidenceRecognitionResult
                {
                    Matches = _matches,
                    MatchArtifacts = _links
                });
    }



    private sealed class FakeClassificationService :
        IEvidenceClassificationService
    {
        public ArtifactId? ArtifactId { get; private set; }
        public string? Classification { get; private set; }
        public ClaimIssueId? ClaimIssueId { get; private set; }

        public RequirementId? RequirementId { get; private set; }

        public Task AssociateRequirementAsync(
            EvidenceClassificationId classificationId,
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
        {
            RequirementId = requirementId;
            return Task.CompletedTask;
        }

        public Task<EvidenceClassification> ClassifyAsync(
            ArtifactId artifactId,
            string classification,
            ClaimIssueId? claimIssueId = null,
            CancellationToken cancellationToken = default)
        {
            ArtifactId = artifactId;
            Classification = classification;
            ClaimIssueId = claimIssueId;

            return Task.FromResult(
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = artifactId,
                    ClaimIssueId = claimIssueId,
                    Classification = classification
                });
        }
    }

    private sealed class FailingDevelopmentRepository :
        FakeDevelopmentRepository
    {
        public override Task AddEvidenceDevelopmentResultAsync(
            EvidenceDevelopmentResult result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Persistence failed.");
    }

    private class FakeDevelopmentRepository :
        IEvidenceDevelopmentPlanRepository
    {
        public EvidenceDevelopmentResult? Result { get; private set; }

        public virtual Task AddEvidenceDevelopmentResultAsync(
            EvidenceDevelopmentResult result,
            CancellationToken cancellationToken = default)
        {
            Result = result;
            return Task.CompletedTask;
        }

        public Task CreateEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> g, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlan p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanArtifactAsync(EvidenceDevelopmentPlanArtifact a, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>> GetEvidenceDevelopmentPlanArtifactsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(EvidenceDevelopmentPlanEvidenceGap g, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>> GetEvidenceDevelopmentPlanEvidenceGapsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddEvidenceDevelopmentPlanRequirementAsync(EvidenceDevelopmentPlanRequirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>> GetEvidenceDevelopmentPlanRequirementsAsync(EvidenceDevelopmentPlanId p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceDevelopmentPlan>> GetEvidenceDevelopmentPlansAsync(ClaimIssueId c, CancellationToken t = default) => throw new NotSupportedException();
    }

    private sealed class FakeGuidanceRepository :
        IEvidenceRequirementGuidanceRepository
    {
        public RequirementId? RequestedRequirementId { get; private set; }

        public Task<IReadOnlyList<EvidenceRequirementGuidance>>
            GetEvidenceRequirementGuidanceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default)
        {
            RequestedRequirementId = requirementId;

            return Task.FromResult<IReadOnlyList<EvidenceRequirementGuidance>>(
                Array.Empty<EvidenceRequirementGuidance>());
        }

        public Task AddEvidenceRequirementGuidanceAsync(
            EvidenceRequirementGuidance guidance,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceRequirementGuidance?>
            GetEvidenceRequirementGuidanceAsync(
                EvidenceRequirementGuidanceId guidanceId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRepository : IEvidenceGapRepository
    {
        private readonly EvidenceGap? _gap;

        public string? UpdatedStatus { get; private set; }

        public FakeRepository(EvidenceGap? gap)
        {
            _gap = gap;
        }

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_gap);

        public Task UpdateEvidenceGapStatusAsync(
            EvidenceGapId id,
            string status,
            CancellationToken cancellationToken = default)
        {
            UpdatedStatus = status;
            return Task.CompletedTask;
        }

        public Task AddEvidenceGapAsync(EvidenceGap gap, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(ClaimIssueId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(RequirementId id, CancellationToken c = default) => throw new NotSupportedException();
    }
}

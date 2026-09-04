using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentIntelligenceCoordinatorTests
{
    [Fact]
    public async Task SummarizeAsync_FailsWhenExecutionIsMissing()
    {
        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                new FakeDevelopmentRepository(),
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_RejectsExecutionForDifferentPlan()
    {
        var repository =
            new FakeDevelopmentRepository
            {
                Execution = new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-other"),
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    WorkflowId =
                        new WorkflowId("workflow-1")
                }
            };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_RejectsExecutionForDifferentGap()
    {
        var repository =
            new FakeDevelopmentRepository
            {
                Execution = new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId =
                        new EvidenceGapId("gap-other"),
                    WorkflowId =
                        new WorkflowId("workflow-1")
                }
            };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_FailsWhenResultIsMissing()
    {
        var repository =
            new FakeDevelopmentRepository
            {
                Execution = new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    WorkflowId =
                        new WorkflowId("workflow-1")
                }
            };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_RejectsResultForDifferentGap()
    {
        var repository =
            new FakeDevelopmentRepository
            {
                Execution = new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    WorkflowId =
                        new WorkflowId("workflow-1")
                },
                Result = new EvidenceDevelopmentResult
                {
                    EvidenceGapId =
                        new EvidenceGapId("gap-other"),
                    RequirementId =
                        new RequirementId("req-1"),
                    EvidenceGuidance = []
                }
            };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_FailsWhenGapIsMissing()
    {
        var repository =
            new FakeDevelopmentRepository
            {
                Execution = new EvidenceDevelopmentExecution
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId("plan-1"),
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    WorkflowId =
                        new WorkflowId("workflow-1")
                },
                Result = new EvidenceDevelopmentResult
                {
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    RequirementId =
                        new RequirementId("req-1"),
                    EvidenceGuidance =
                        Array.Empty<EvidenceRequirementGuidance>()
                }
            };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                new FakeGapRepository(),
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_RejectsWrongReturnedGapIdentity()
    {
        var repository = new FakeDevelopmentRepository
        {
            Execution = new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId =
                    new EvidenceDevelopmentPlanId("plan-1"),
                EvidenceGapId = new EvidenceGapId("gap-1"),
                WorkflowId = new WorkflowId("workflow-1")
            },
            Result = new EvidenceDevelopmentResult
            {
                EvidenceGapId = new EvidenceGapId("gap-1"),
                RequirementId = new RequirementId("req-1"),
                EvidenceGuidance = []
            }
        };

        var gaps = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-other"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                RequirementId = new RequirementId("req-1"),
                Description = "Missing evidence."
            }
        };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                gaps,
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_RejectsResultForDifferentRequirement()
    {
        var repository = new FakeDevelopmentRepository
        {
            Execution = new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId =
                    new EvidenceDevelopmentPlanId("plan-1"),
                EvidenceGapId = new EvidenceGapId("gap-1"),
                WorkflowId = new WorkflowId("workflow-1")
            },
            Result = new EvidenceDevelopmentResult
            {
                EvidenceGapId = new EvidenceGapId("gap-1"),
                RequirementId = new RequirementId("req-other"),
                EvidenceGuidance = []
            }
        };

        var gaps = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                RequirementId = new RequirementId("req-1"),
                Description = "Missing evidence."
            }
        };

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                gaps,
                new FakeExecutor());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext()));
    }

    [Fact]
    public async Task SummarizeAsync_UsesPersistedDevelopmentResult()
    {
        var repository = new FakeDevelopmentRepository
        {
            Execution = new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId = new EvidenceDevelopmentPlanId("plan-1"),
                EvidenceGapId = new EvidenceGapId("gap-1"),
                WorkflowId = new WorkflowId("workflow-1")
            },
            Result = new EvidenceDevelopmentResult
            {
                EvidenceGapId = new EvidenceGapId("gap-1"),
                RequirementId = new RequirementId("req-1"),
                EvidenceGuidance =
                [
                    new EvidenceRequirementGuidance
                    {
                        Id = new EvidenceRequirementGuidanceId("guide-1"),
                        RequirementId = new RequirementId("req-1"),
                        EvidenceClassification = "medical",
                        GuidanceRole = "supporting",
                        Description = "Obtain nexus opinion."
                    }
                ]
            }
        };

        var gapRepository = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                RequirementId = new RequirementId("req-1"),
                Description = "Missing evidence."
            },
            Artifacts =
            [
                new EvidenceGapArtifact
                {
                    EvidenceGapId = new EvidenceGapId("gap-1"),
                    ArtifactId = new ArtifactId("artifact-gap-1"),
                    Role = "supporting"
                }
            ]
        };

        var executor = new FakeExecutor();

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                gapRepository,
                executor);

        var result =
            await coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                TestContext());

        Assert.True(result.Success);
        Assert.Equal("summary", result.Output);
        Assert.Contains(
            "medical / supporting: Obtain nexus opinion.",
            executor.Request!.Text);

        Assert.Contains(
            new ArtifactId("artifact-gap-1"),
            executor.Context!.InputArtifactIds);
    }

    [Fact]
    public async Task SummarizeAsync_PreservesContextAndGapArtifacts()
    {
        var repository = new FakeDevelopmentRepository
        {
            Execution = new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId =
                    new EvidenceDevelopmentPlanId("plan-1"),
                EvidenceGapId =
                    new EvidenceGapId("gap-1"),
                WorkflowId =
                    new WorkflowId("workflow-1")
            },
            Result = new EvidenceDevelopmentResult
            {
                EvidenceGapId =
                    new EvidenceGapId("gap-1"),
                RequirementId =
                    new RequirementId("req-1"),
                EvidenceGuidance =
                    Array.Empty<EvidenceRequirementGuidance>()
            }
        };

        var gapRepository = new FakeGapRepository
        {
            Gap = new EvidenceGap
            {
                Id = new EvidenceGapId("gap-1"),
                ClaimIssueId = new ClaimIssueId("issue-1"),
                RequirementId = new RequirementId("req-1"),
                Description = "Missing evidence."
            },
            Artifacts =
            [
                new EvidenceGapArtifact
                {
                    EvidenceGapId =
                        new EvidenceGapId("gap-1"),
                    ArtifactId =
                        new ArtifactId("artifact-gap-1"),
                    Role = "supporting"
                }
            ]
        };

        var executor = new FakeExecutor();

        var coordinator =
            new EvidenceDevelopmentIntelligenceCoordinator(
                repository,
                gapRepository,
                executor);

        var context = new IntelligenceExecutionContext(
            "security-steward",
            new IntelligenceCorrelationId("test-1"),
            new ProtectionClassificationId("confidential"),
            [
                new ArtifactId("artifact-context-1")
            ]);

        var result =
            await coordinator.SummarizeAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new EvidenceGapId("gap-1"),
                context);

        Assert.True(result.Success);

        Assert.Contains(
            new ArtifactId("artifact-context-1"),
            executor.Context!.InputArtifactIds);

        Assert.Contains(
            new ArtifactId("artifact-gap-1"),
            executor.Context.InputArtifactIds);

        Assert.Equal(
            2,
            executor.Context.InputArtifactIds.Count());
    }

    private sealed class FakeDevelopmentRepository :
        IEvidenceDevelopmentPlanRepository
    {
        public EvidenceDevelopmentExecution? Execution { get; set; }

        public EvidenceDevelopmentResult? Result { get; set; }

        public Task<EvidenceDevelopmentExecution?>
            GetEvidenceDevelopmentExecutionAsync(
                EvidenceDevelopmentPlanId planId,
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default)
            => Task.FromResult(Execution);

        public Task<EvidenceDevelopmentResult?>
            GetEvidenceDevelopmentResultAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task CreateEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> gaps,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlan plan,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceDevelopmentPlan?> GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanArtifactAsync(
            EvidenceDevelopmentPlanArtifact artifact,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
            GetEvidenceDevelopmentPlanArtifactsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
            EvidenceDevelopmentPlanEvidenceGap gap,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
            GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddEvidenceDevelopmentPlanRequirementAsync(
            EvidenceDevelopmentPlanRequirement requirement,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
            GetEvidenceDevelopmentPlanRequirementsAsync(
                EvidenceDevelopmentPlanId planId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceDevelopmentPlan>>
            GetEvidenceDevelopmentPlansAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeGapRepository :
        IEvidenceGapRepository
    {
        public EvidenceGap? Gap { get; set; }

        public IReadOnlyList<EvidenceGapArtifact> Artifacts
        { get; set; } = Array.Empty<EvidenceGapArtifact>();

        public Task AddEvidenceGapAsync(
            EvidenceGap gap,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EvidenceGap?> GetEvidenceGapAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Gap);

        public Task<IReadOnlyList<EvidenceGapArtifact>>
            GetEvidenceGapArtifactsAsync(
                EvidenceGapId evidenceGapId,
                CancellationToken cancellationToken = default)
            => Task.FromResult(Artifacts);

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<EvidenceGap>> GetEvidenceGapsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeExecutor :
        IIntelligenceCapabilityExecutor<TextSummarizationRequest, string>
    {
        public TextSummarizationRequest? Request { get; private set; }

        public IntelligenceExecutionContext? Context
        { get; private set; }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            Context = context;

            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = "summary",
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
                    }
                });
        }
    }

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("test-1"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());
}

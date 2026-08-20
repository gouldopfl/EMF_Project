using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentIntelligenceServiceTests
{

    [Fact]
    public async Task SummarizeAsync_ReturnsSuccessfulSummary()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Output = "summary",
            RequiresReview = true,
            Metadata = TestMetadata()
        };

        var executor =
            new FakeExecutor(result);

        var service =
            new EvidenceDevelopmentIntelligenceService(
                executor);

        var actual =
            await service.SummarizeAsync(
                TestGap(),
                [new EvidenceRequirementGuidance
                {
                    Id = new EvidenceRequirementGuidanceId("guide-1"),
                    RequirementId = new RequirementId("req-1"),
                    EvidenceClassification = "medical",
                    GuidanceRole = "supporting",
                    Description = "Obtain nexus opinion."
                }],
                TestContext());

        Assert.True(actual.Succeeded);
        Assert.Equal("summary", actual.Summary);
        Assert.True(actual.RequiresReview);
        Assert.Same(result.Metadata, actual.Metadata);

        Assert.Equal(
            IntelligenceCapabilityIds.TextSummarization,
            executor.CapabilityId);

        Assert.NotNull(executor.Context);
        Assert.NotNull(executor.Request);

        Assert.Contains(
            "Gap: Missing evidence.",
            executor.Request!.Text);

        Assert.Contains(
            "Requirement ID: req-1",
            executor.Request.Text);

        Assert.Contains(
            "medical / supporting: Obtain nexus opinion.",
            executor.Request.Text);
    }

    [Fact]
    public async Task SummarizeAsync_FailsWhenOutputIsBlank()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Output = "   ",
            RequiresReview = true,
            Metadata = TestMetadata()
        };

        var service =
            new EvidenceDevelopmentIntelligenceService(
                new FakeExecutor(result));

        var actual =
            await service.SummarizeAsync(
                TestGap(),
                [new EvidenceRequirementGuidance
                {
                    Id = new EvidenceRequirementGuidanceId("guide-1"),
                    RequirementId = new RequirementId("req-1"),
                    EvidenceClassification = "medical",
                    GuidanceRole = "supporting",
                    Description = "Obtain nexus opinion."
                }],
                TestContext());

        Assert.False(actual.Succeeded);
    }

    [Fact]
    public async Task SummarizeAsync_PropagatesCapabilityFailure()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = false,
            Message = "Unavailable.",
            RequiresReview = true,
            Metadata = TestMetadata()
        };

        var service =
            new EvidenceDevelopmentIntelligenceService(
                new FakeExecutor(result));

        var actual =
            await service.SummarizeAsync(
                TestGap(),
                Array.Empty<EvidenceRequirementGuidance>(),
                TestContext());

        Assert.False(actual.Succeeded);
        Assert.Equal("Unavailable.", actual.Message);
    }

    [Fact]
    public void Constructor_RejectsNullExecutor()
    {
        Assert.Throws<ArgumentNullException>(
            () => new EvidenceDevelopmentIntelligenceService(null!));
    }
    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<TextSummarizationRequest, string>
    {
        public IntelligenceCapabilityId CapabilityId { get; private set; }

        public TextSummarizationRequest? Request { get; private set; }

        public IntelligenceExecutionContext? Context { get; private set; }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            CapabilityId = capabilityId;
            Request = request;
            Context = context;

            return Task.FromResult(result);
        }
    }

    private static EvidenceGap TestGap() => new()
    {
        Id = new EvidenceGapId("gap-1"),
        ClaimIssueId = new ClaimIssueId("issue-1"),
        RequirementId = new RequirementId("req-1"),
        Description = "Missing evidence."
    };

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("test-1"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

    private static IntelligenceExecutionMetadata TestMetadata() => new()
    {
        CapabilityId = IntelligenceCapabilityIds.TextSummarization,
        ProviderId = new IntelligenceProviderId("development.local"),
        CorrelationId = new IntelligenceCorrelationId("test-1"),
        EngineName = "test",
        StartedUtc = DateTimeOffset.UtcNow,
        CompletedUtc = DateTimeOffset.UtcNow
    };
}

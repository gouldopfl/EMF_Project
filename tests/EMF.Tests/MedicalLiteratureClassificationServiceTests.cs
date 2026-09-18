using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class MedicalLiteratureClassificationServiceTests
{
    [Fact]
    public async Task ClassifyAsync_MapsGroundedOutput()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Output = """
                {
                  "classifications": [{
                    "requirementId": "requirement-a",
                    "guidanceRole": "SupportsRequirement",
                    "description": "Supports the requirement.",
                    "sourceSegmentIds": ["S001"]
                  }]
                }
                """,
            RequiresReview = true,
            Metadata = Metadata()
        };

        var executor = new FakeExecutor(result);
        var service =
            new MedicalLiteratureClassificationService(executor);

        var source = new MedicalLiteratureSource
        {
            Id = new("source-a"),
            Title = "Test article",
            Authors = "Test Author",
            Publication = "Test Journal",
            VaAffiliated = false,
            VaFunded = false,
            PeerReviewed = true
        };

        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate requirement."
        };

        var actual = await service.ClassifyAsync(
            source,
            new ArtifactId("artifact-a"),
            "PTSD was associated with OSA.",
            [requirement],
            Context());

        var proposal = Assert.IsType<
            MedicalLiteratureClassificationProposal>(
                actual.Proposal);

        var classification =
            Assert.Single(proposal.Classifications);

        Assert.Equal(
            requirement.Id,
            classification.RequirementId);
        Assert.Equal(
            EvidenceGuidanceRoles.SupportsRequirement,
            classification.GuidanceRole);

        var excerpt = Assert.Single(classification.SourceExcerpts);
        Assert.Equal("PTSD was associated with OSA.", excerpt.Text);
        Assert.Equal(0, excerpt.StartOffset);
        Assert.Equal(excerpt.Text.Length, excerpt.Length);

        Assert.NotNull(executor.Request);
        Assert.Contains(
            "[S001] PTSD was associated with OSA.",
            executor.Request!.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "sourceSegmentIds",
            executor.Request.JsonSchema,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sourceExcerpts",
            executor.Request.JsonSchema,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassifyAsync_PreservesBalancingEvidenceAsClarifies()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Output = """
                {
                  "classifications": [{
                    "requirementId": "requirement-a",
                    "guidanceRole": "Clarifies",
                    "description": "Provides balancing evidence.",
                    "sourceSegmentIds": ["S001"]
                  }]
                }
                """,
            RequiresReview = true,
            Metadata = Metadata()
        };

        var executor = new FakeExecutor(result);
        var service =
            new MedicalLiteratureClassificationService(executor);

        var actual = await service.ClassifyAsync(
            Source(),
            new ArtifactId("artifact-a"),
            "The study did not find a statistically significant association.",
            [Requirement()],
            Context());

        var proposal = Assert.IsType<
            MedicalLiteratureClassificationProposal>(
                actual.Proposal);
        var classification = Assert.Single(proposal.Classifications);

        Assert.Equal(
            EvidenceGuidanceRoles.Clarifies,
            classification.GuidanceRole);
        Assert.Contains(
            "Use Clarifies when the article materially qualifies, limits, " +
            "contradicts, or provides balancing context",
            executor.Request!.Instruction,
            StringComparison.Ordinal);
        Assert.Contains(
            "Return no classification only when the article is not materially " +
            "relevant",
            executor.Request.Instruction,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassifyAsync_MapsSourceSegmentIdsToExactOffsets()
    {
        var service =
            new MedicalLiteratureClassificationService(
                new FakeExecutor(
                    ClassificationResult("S002")));

        var actual = await service.ClassifyAsync(
            Source(),
            new ArtifactId("artifact-a"),
            "Prefix.\nPTSD was associated with OSA.\nSuffix.",
            [Requirement()],
            Context());

        var proposal = Assert.IsType<
            MedicalLiteratureClassificationProposal>(
                actual.Proposal);
        var excerpt =
            Assert.Single(
                Assert.Single(proposal.Classifications)
                    .SourceExcerpts);

        Assert.Equal("PTSD was associated with OSA.", excerpt.Text);
        Assert.Equal(8, excerpt.StartOffset);
        Assert.Equal(29, excerpt.Length);
    }

    [Fact]
    public async Task ClassifyAsync_RejectsUnknownGroundingSegment()
    {
        var service =
            new MedicalLiteratureClassificationService(
                new FakeExecutor(
                    ClassificationResult("S999")));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ClassifyAsync(
                    Source(),
                    new ArtifactId("artifact-a"),
                    "Article text.",
                    [Requirement()],
                    Context()));

        Assert.Contains(
            "does not exist in the source document",
            exception.Message,
            StringComparison.Ordinal);
    }

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest, string>
    {
        public TextStructuredExtractionRequest? Request { get; private set; }

        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private static IntelligenceExecutionContext Context() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("literature-test"),
            new ProtectionClassificationId("confidential"),
            []);

    private static IntelligenceExecutionMetadata Metadata() =>
        new()
        {
            CapabilityId =
                IntelligenceCapabilityIds.TextStructuredExtraction,
            ProviderId =
                new IntelligenceProviderId("development.test"),
            CorrelationId =
                new IntelligenceCorrelationId("literature-test"),
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };

    private static IntelligenceCapabilityResult<string>
        ClassificationResult(string segmentId) =>
        new()
        {
            Success = true,
            Output =
                $$"""
                  {
                    "classifications": [{
                      "requirementId": "requirement-a",
                      "guidanceRole": "Corroborates",
                      "description": "Description.",
                      "sourceSegmentIds": ["{{segmentId}}"]
                    }]
                  }
                  """,
            RequiresReview = true,
            Metadata = Metadata()
        };

    private static MedicalLiteratureSource Source() =>
        new()
        {
            Id = new("source-a"),
            Title = "Test article",
            Authors = "Test Author",
            Publication = "Test Journal",
            VaAffiliated = false,
            VaFunded = false,
            PeerReviewed = true
        };

    private static Requirement Requirement() =>
        new()
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate requirement."
        };

    [Fact]
    public async Task ClassifyAsync_RejectsUnknownRequirement()
    {
        var result = new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Output = """
                {
                  "classifications": [{
                    "requirementId": "invented-requirement",
                    "guidanceRole": "SupportsRequirement",
                    "description": "Description.",
                    "sourceSegmentIds": ["S001"]
                  }]
                }
                """,
            RequiresReview = true,
            Metadata = Metadata()
        };

        var service =
            new MedicalLiteratureClassificationService(
                new FakeExecutor(result));

        var source = new MedicalLiteratureSource
        {
            Id = new("source-a"),
            Title = "Test article",
            Authors = "Test Author",
            Publication = "Test Journal",
            VaAffiliated = false,
            VaFunded = false,
            PeerReviewed = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ClassifyAsync(
                source,
                new ArtifactId("artifact-a"),
                "Article text.",
                [],
                Context()));
    }
}

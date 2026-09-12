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
                    "sourceExcerpts": [{
                      "text": "PTSD was associated with OSA.",
                      "startOffset": 0,
                      "length": 29
                    }]
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
    }

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
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
                    "sourceExcerpts": [{
                      "text": "Article",
                      "startOffset": 0,
                      "length": 7
                    }]
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

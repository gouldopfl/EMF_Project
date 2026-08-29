using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VaDecisionDocumentInterpretationCoordinatorTests
{
    [Fact]
    public async Task InterpretAsync_ExtractsArtifactText()
    {
        var artifactId = new ArtifactId("decision-1");

        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = false,
                Message = "Unavailable.",
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var extractor =
            new FakeTextExtractor("Decision text.");

        var executor =
            new FakeExecutor(capabilityResult);

        var coordinator =
            new VaDecisionDocumentInterpretationCoordinator(
                extractor,
                executor);

        var result =
            await coordinator.InterpretAsync(
                artifactId,
                TestContext());

        Assert.Same(
            capabilityResult,
            result.IntelligenceResult);

        Assert.Equal(
            artifactId,
            extractor.ArtifactId);

        Assert.Contains(
            artifactId,
            executor.Context!.InputArtifactIds);
    }


    [Fact]
    public async Task InterpretAsync_RejectsMissingArtifactText()
    {
        var executor =
            new FakeExecutor(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    RequiresReview = true,
                    Metadata = TestMetadata()
                });

        var coordinator =
            new VaDecisionDocumentInterpretationCoordinator(
                new FakeTextExtractor(null),
                executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.InterpretAsync(
                new ArtifactId("decision-1"),
                TestContext()));

        Assert.Null(executor.Context);
    }


    private sealed class FakeTextExtractor(string? text) :
        IArtifactTextExtractor
    {
        public ArtifactId? ArtifactId { get; private set; }

        public Task<string?> ExtractTextAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            ArtifactId = artifactId;
            return Task.FromResult(text);
        }
    }

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public IntelligenceExecutionContext? Context
        { get; private set; }

        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(result);
        }
    }

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("decision-test-1"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

    private static IntelligenceExecutionMetadata TestMetadata() =>
        new()
        {
            CapabilityId =
                IntelligenceCapabilityIds
                    .TextStructuredExtraction,
            ProviderId =
                new IntelligenceProviderId("development.test"),
            CorrelationId =
                new IntelligenceCorrelationId(
                    "decision-test-1"),
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
}


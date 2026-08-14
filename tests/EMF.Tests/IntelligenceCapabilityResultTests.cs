using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceCapabilityResultTests
{
    [Fact]
    public void Result_ExposesTraceabilityAndReviewFacts()
    {
        var completedUtc =
            new DateTimeOffset(
                2026,
                8,
                14,
                12,
                0,
                0,
                TimeSpan.Zero);

        var result =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Message = "Completed with review.",
                Output = "Derived summary",
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId =
                            new IntelligenceCapabilityId(
                                "document.summarize"),
                        ProviderId =
                            new IntelligenceProviderId(
                                "development"),
                        CorrelationId =
                            new IntelligenceCorrelationId(
                                "operation-001"),
                        EngineName = "test-engine",
                        EngineVersion = "1.0",
                        ProviderOperationId =
                            "provider-operation-001",
                        StartedUtc =
                            completedUtc.AddSeconds(-2),
                        CompletedUtc = completedUtc
                    },
                SourceArtifactIds =
                [
                    new ArtifactId("artifact-001")
                ],
                Warnings =
                [
                    "Human review required."
                ],
                RequiresReview = true
            };

        IOperationResult operationResult = result;

        Assert.True(operationResult.Success);
        Assert.Equal(
            "Derived summary",
            result.Output);

        Assert.Equal(
            "development",
            result.Metadata.ProviderId.Value);

        Assert.Equal(
            "test-engine",
            result.Metadata.EngineName);

        Assert.Equal(
            "artifact-001",
            Assert.Single(
                result.SourceArtifactIds).Value);

        Assert.Single(result.Warnings);
        Assert.True(result.RequiresReview);
    }
}

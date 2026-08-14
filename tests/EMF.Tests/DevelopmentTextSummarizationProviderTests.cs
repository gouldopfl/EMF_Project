using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    DevelopmentTextSummarizationProviderTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsTraceableSummary()
    {
        var artifactId =
            new ArtifactId(
                "artifact-001");

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId]);

        var provider =
            new DevelopmentTextSummarizationProvider();

        var result =
            await provider.ExecuteAsync(
                new TextSummarizationRequest(
                    "Alpha beta gamma",
                    11),
                context);

        Assert.True(result.Success);
        Assert.Equal(
            "Alpha beta…",
            result.Output);

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextSummarization,
            result.Metadata.CapabilityId);

        Assert.Equal(
            provider.ProviderId,
            result.Metadata.ProviderId);

        Assert.Equal(
            context.CorrelationId,
            result.Metadata.CorrelationId);

        Assert.Equal(
            "deterministic-extractive",
            result.Metadata.EngineName);

        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));

        Assert.Single(result.Warnings);
        Assert.True(result.RequiresReview);
    }
}

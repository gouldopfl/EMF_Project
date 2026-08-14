using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    DevelopmentTextSegmentationProviderTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesOverlappingSegments()
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
            new DevelopmentTextSegmentationProvider();

        var result =
            await provider.ExecuteAsync(
                new TextSegmentationRequest(
                    "ABCDEFGHIJ",
                    4,
                    1),
                context);

        Assert.True(result.Success);

        var segments =
            Assert.IsAssignableFrom<
                IReadOnlyList<TextSegment>>(
                result.Output);

        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal(0, segment.Index);
                Assert.Equal(0, segment.StartOffset);
                Assert.Equal("ABCD", segment.Text);
            },
            segment =>
            {
                Assert.Equal(1, segment.Index);
                Assert.Equal(3, segment.StartOffset);
                Assert.Equal("DEFG", segment.Text);
            },
            segment =>
            {
                Assert.Equal(2, segment.Index);
                Assert.Equal(6, segment.StartOffset);
                Assert.Equal("GHIJ", segment.Text);
            });

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextSegmentation,
            result.Metadata.CapabilityId);

        Assert.Equal(
            provider.ProviderId,
            result.Metadata.ProviderId);

        Assert.Equal(
            artifactId,
            Assert.Single(
                result.SourceArtifactIds));
    }
}

using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    DevelopmentTextKeywordExtractionProviderTests
{
    [Fact]
    public async Task ExecuteAsync_RanksTermsAndPreservesOffsets()
    {
        var provider =
            new DevelopmentTextKeywordExtractionProvider();

        var result =
            await provider.ExecuteAsync(
                new TextKeywordExtractionRequest(
                    "Evidence review evidence policy " +
                    "review evidence custom.",
                    2,
                    4,
                    ["review"]),
                CreateContext());

        Assert.True(result.Success);
        Assert.NotNull(result.Output);

        Assert.Equal(
            ["evidence", "policy"],
            result.Output!
                .Select(keyword => keyword.Term)
                .ToArray());

        var evidence = result.Output[0];

        Assert.Equal(3, evidence.Occurrences);
        Assert.Equal(
            [0, 16, 39],
            evidence.Offsets);

        Assert.Equal(
            IntelligenceCapabilityIds
                .TextKeywordExtraction,
            result.Metadata.CapabilityId);

        Assert.Equal(
            provider.ProviderId,
            result.Metadata.ProviderId);

        Assert.Equal(
            "artifact-001",
            Assert.Single(
                result.SourceArtifactIds).Value);

        Assert.Single(result.Warnings);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation()
    {
        var provider =
            new DevelopmentTextKeywordExtractionProvider();

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => provider.ExecuteAsync(
                new TextKeywordExtractionRequest(
                    "Evidence review.",
                    5),
                CreateContext(),
                cancellation.Token));
    }

    private static IntelligenceExecutionContext
        CreateContext()
    {
        return new IntelligenceExecutionContext(
            "security-steward",
            new IntelligenceCorrelationId(
                "operation-001"),
            new ProtectionClassificationId(
                "confidential"),
            [
                new ArtifactId(
                    "artifact-001")
            ]);
    }
}

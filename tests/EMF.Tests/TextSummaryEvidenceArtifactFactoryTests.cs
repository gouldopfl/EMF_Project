using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class TextSummaryEvidenceArtifactFactoryTests
{
    [Fact]
    public void Create_ProducesDeterministicEvidenceArtifact()
    {
        var factory =
            new TextSummaryEvidenceArtifactFactory();

        var createdUtc =
            new DateTimeOffset(
                2026, 8, 20, 19, 0, 0,
                TimeSpan.Zero);

        var first =
            factory.Create(
                "Evidence summary.",
                "Veterans evidence summary",
                createdUtc);

        var second =
            factory.Create(
                "Evidence summary.",
                "Veterans evidence summary",
                createdUtc);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(
            first.Fingerprint,
            second.Fingerprint);
        Assert.Equal(
            "text-summary",
            first.ArtifactType);
        Assert.Equal(
            "Evidence summary.",
            first.Metadata["summary"]);
    }
}

using EMF.Intelligence.Capabilities;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class TextInsightEvidenceArtifactFactoryTests
{
    [Fact]
    public void Create_ProducesDeterministicEvidenceArtifact()
    {
        var factory =
            new TextInsightEvidenceArtifactFactory();

        var insight =
            new TextInsight(
                "Evidence summary.",
                [
                    new TextKeyword(
                        "evidence",
                        [0, 20])
                ]);

        var createdUtc =
            new DateTimeOffset(
                2026, 8, 14, 19, 0, 0,
                TimeSpan.Zero);

        var first =
            factory.Create(
                insight,
                "Source text insight",
                createdUtc);

        var second =
            factory.Create(
                insight,
                "Source text insight",
                createdUtc);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(
            first.Fingerprint,
            second.Fingerprint);
        Assert.Equal(
            "text-insight",
            first.ArtifactType);
        Assert.Equal(
            "Evidence summary.",
            first.Metadata["summary"]);
    }
}

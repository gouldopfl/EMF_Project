using EMF.Core.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceExecutionContextTests
{
    [Fact]
    public void Constructor_PreservesExecutionFacts()
    {
        var originalArtifactId =
            new ArtifactId("artifact-001");

        ArtifactId[] inputs =
        [
            originalArtifactId
        ];

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                inputs,
                new AgentId(
                    "review-agent"));

        inputs[0] =
            new ArtifactId("artifact-changed");

        Assert.Equal(
            "security-steward",
            context.SubjectId);

        Assert.Equal(
            "operation-001",
            context.CorrelationId.Value);

        Assert.Equal(
            "confidential",
            context.ProtectionClassificationId.Value);

        Assert.Equal(
            originalArtifactId,
            Assert.Single(
                context.InputArtifactIds));

        Assert.Equal(
            "review-agent",
            context.AgentId!.Value.Value);
    }

    [Fact]
    public void Constructor_RejectsDuplicateArtifacts()
    {
        var artifactId =
            new ArtifactId("artifact-001");

        Assert.Throws<ArgumentException>(
            () =>
                new IntelligenceExecutionContext(
                    "security-steward",
                    new IntelligenceCorrelationId(
                        "operation-001"),
                    new ProtectionClassificationId(
                        "confidential"),
                    [artifactId, artifactId]));
    }
}

using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    ConfiguredIntelligenceProviderRoutingPolicyBoundaryTests
{
    [Fact]
    public void Constructor_RejectsNullGrants()
    {
        Assert.Throws<ArgumentNullException>(
            () => new
                ConfiguredIntelligenceProviderRoutingPolicy(
                    null!));
    }

    [Fact]
    public void Constructor_RejectsDefaultGrant()
    {
        IntelligenceProviderRoutingGrant[] grants =
        [
            default
        ];

        Assert.Throws<ArgumentException>(
            () => new
                ConfiguredIntelligenceProviderRoutingPolicy(
                    grants));
    }

    [Theory]
    [InlineData(
        "provider-two",
        "document-analysis",
        "confidential")]
    [InlineData(
        "provider-one",
        "image-analysis",
        "confidential")]
    [InlineData(
        "provider-one",
        "document-analysis",
        "restricted")]
    public async Task
        EvaluateAsync_DeniesWhenAnyRoutingAxisDiffers(
            string requestedProvider,
            string requestedCapability,
            string requestedClassification)
    {
        var configuredProviderId =
            new IntelligenceProviderId(
                "provider-one");

        var configuredCapabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var configuredClassificationId =
            new ProtectionClassificationId(
                "confidential");

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        configuredProviderId,
                        configuredCapabilityId,
                        configuredClassificationId)
                ]);

        var decision =
            await policy.EvaluateAsync(
                new IntelligenceProviderId(
                    requestedProvider),
                new IntelligenceCapabilityId(
                    requestedCapability),
                CreateContext(
                    new ProtectionClassificationId(
                        requestedClassification)));

        Assert.False(decision.Permitted);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsNullContext()
    {
        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                Array.Empty<
                    IntelligenceProviderRoutingGrant>());

        await Assert.ThrowsAsync<
            ArgumentNullException>(
            () => policy.EvaluateAsync(
                new IntelligenceProviderId(
                    "provider-one"),
                new IntelligenceCapabilityId(
                    "document-analysis"),
                null!));
    }

    private static IntelligenceExecutionContext
        CreateContext(
            ProtectionClassificationId
                classificationId)
    {
        return new IntelligenceExecutionContext(
            "security-steward",
            new IntelligenceCorrelationId(
                "operation-001"),
            classificationId,
            []);
    }
}

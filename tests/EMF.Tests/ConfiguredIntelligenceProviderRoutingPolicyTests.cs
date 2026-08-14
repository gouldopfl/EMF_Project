using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    ConfiguredIntelligenceProviderRoutingPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_PermitsExactGrant()
    {
        var providerId =
            new IntelligenceProviderId(
                "provider-one");

        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        providerId,
                        capabilityId,
                        classificationId)
                ]);

        var decision =
            await policy.EvaluateAsync(
                providerId,
                capabilityId,
                CreateContext(classificationId));

        Assert.True(decision.Permitted);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DeniesUnconfiguredRoute()
    {
        var providerId =
            new IntelligenceProviderId(
                "provider-one");

        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                Array.Empty<
                    IntelligenceProviderRoutingGrant>());

        var decision =
            await policy.EvaluateAsync(
                providerId,
                capabilityId,
                CreateContext(
                    new ProtectionClassificationId(
                        "confidential")));

        Assert.False(decision.Permitted);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_PropagatesCancellation()
    {
        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                Array.Empty<
                    IntelligenceProviderRoutingGrant>());

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => policy.EvaluateAsync(
                new IntelligenceProviderId(
                    "provider-one"),
                new IntelligenceCapabilityId(
                    "document-analysis"),
                CreateContext(
                    new ProtectionClassificationId(
                        "confidential")),
                cancellation.Token));
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

using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsWrongProviderMetadata()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-one"));

        var context = CreateContext();

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        capabilityId,
                        context.ProtectionClassificationId)
                ]);

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    string,
                    string>(
                    [provider],
                    policy));

        var completedUtc =
            new DateTimeOffset(
                2026, 8, 14, 12, 0, 0,
                TimeSpan.Zero);

        provider.Result =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output = "result-content",
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId = capabilityId,
                        ProviderId =
                            new IntelligenceProviderId(
                                "wrong-provider"),
                        CorrelationId =
                            context.CorrelationId,
                        EngineName = "test-engine",
                        StartedUtc =
                            completedUtc.AddSeconds(-1),
                        CompletedUtc = completedUtc
                    }
            };

        await Assert.ThrowsAsync<
            IntelligenceCapabilityResultValidationException>(
            () => executor.ExecuteAsync(
                capabilityId,
                "request-content",
                context));
    }
}

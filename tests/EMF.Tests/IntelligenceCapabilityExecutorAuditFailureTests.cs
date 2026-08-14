using EMF.Intelligence.Execution;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotHideAuditFailure()
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

        var auditFailure =
            new InvalidOperationException(
                "Audit write failed.");

        var auditSink =
            new RecordingAuditSink
            {
                Failure = auditFailure
            };

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
                    policy),
                auditSink);

        var thrown =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Same(auditFailure, thrown);
    }
}

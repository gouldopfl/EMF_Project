using EMF.Intelligence.Execution;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuditsProviderFailure()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var failure =
            new InvalidOperationException(
                "Provider failed.");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-one"))
            {
                Failure = failure
            };

        var context = CreateContext();
        var auditSink =
            new RecordingAuditSink();

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
                new RecordingAuthorizationPolicy(),
                auditSink);

        var thrown =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Same(failure, thrown);

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);

        Assert.Equal(
            provider.ProviderId.Value,
            audit.Destination);
    }
}

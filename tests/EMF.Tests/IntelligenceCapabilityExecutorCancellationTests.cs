using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuditsCancellation()
    {
        var auditSink =
            new RecordingAuditSink();

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                Array.Empty<
                    IIntelligenceCapabilityProvider<
                        string,
                        string>>(),
                new ConfiguredIntelligenceProviderRoutingPolicy(
                    Array.Empty<
                        IntelligenceProviderRoutingGrant>()));

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                router,
                new RecordingAuthorizationPolicy(),
                auditSink);

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => executor.ExecuteAsync(
                new IntelligenceCapabilityId(
                    "document-analysis"),
                "request-content",
                CreateContext(),
                cancellation.Token));

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            audit.Outcome);
    }

    [Fact]
    public async Task
        ExecuteAsync_AuditsProviderCancellation()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var providerCancellation =
            new OperationCanceledException(
                "Provider cancelled.");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-one"))
            {
                Failure = providerCancellation
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
                OperationCanceledException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Same(
            providerCancellation,
            thrown);

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            audit.Outcome);

        Assert.Equal(
            AuthorizationDecision.Allow,
            audit.PolicyDecision);

        Assert.Equal(
            provider.ProviderId.Value,
            audit.Destination);
    }
}

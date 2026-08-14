using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;

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
}

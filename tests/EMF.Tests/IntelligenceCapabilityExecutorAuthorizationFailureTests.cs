using EMF.Core.Models.Identities;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuditsAuthorizationFailure()
    {
        var capabilityId =
            new IntelligenceCapabilityId("document-analysis");

        var artifactId =
            new ArtifactId("artifact-authorization-failure");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId("provider-one"));

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId("operation-001"),
                new ProtectionClassificationId("confidential"),
                [artifactId]);

        var failure =
            new InvalidOperationException(
                "Authorization policy failed.");

        var authorizationPolicy =
            new RecordingAuthorizationPolicy
            {
                Failure = failure
            };

        var auditSink = new RecordingAuditSink();

        var executor =
            new IntelligenceCapabilityExecutor<string, string>(
                new IntelligenceCapabilityProviderRouter<string, string>(
                    [provider],
                    new ConfiguredIntelligenceProviderRoutingPolicy(
                        Array.Empty<
                            IntelligenceProviderRoutingGrant>())),
                authorizationPolicy,
                auditSink);

        var thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Same(failure, thrown);
        Assert.Null(provider.LastRequest);
        Assert.Single(authorizationPolicy.Requests);

        var audit = Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);

        Assert.Null(audit.PolicyDecision);
        Assert.Null(audit.Destination);
    }
}

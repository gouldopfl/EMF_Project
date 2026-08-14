using EMF.Core.Models.Identities;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DeniesUnauthorizedInput()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var artifactId =
            new ArtifactId(
                "artifact-denied");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-one"));

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [artifactId]);

        var authorizationPolicy =
            new RecordingAuthorizationPolicy
            {
                Decision =
                    AuthorizationDecision.Deny
            };

        var auditSink =
            new RecordingAuditSink();

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    string,
                    string>(
                    [provider],
                    new ConfiguredIntelligenceProviderRoutingPolicy(
                        Array.Empty<
                            IntelligenceProviderRoutingGrant>())),
                authorizationPolicy,
                auditSink);

        var exception =
            await Assert.ThrowsAsync<
                IntelligenceInputAuthorizationException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Equal(
            artifactId,
            exception.ArtifactId);

        Assert.Null(
            provider.LastRequest);

        Assert.Single(
            authorizationPolicy.Requests);

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Denied,
            audit.Outcome);

        Assert.Equal(
            AuthorizationDecision.Deny,
            audit.PolicyDecision);
    }
}

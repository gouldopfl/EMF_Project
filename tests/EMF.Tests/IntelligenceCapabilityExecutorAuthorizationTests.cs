using EMF.Core.Models.Identities;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuthorizesEveryInputArtifact()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

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
                [
                    new ArtifactId("artifact-001"),
                    new ArtifactId("artifact-002")
                ]);

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        capabilityId,
                        context.ProtectionClassificationId)
                ]);

        var authorizationPolicy =
            new RecordingAuthorizationPolicy();

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    string,
                    string>(
                    [provider],
                    routingPolicy),
                authorizationPolicy,
                new RecordingAuditSink());

        await executor.ExecuteAsync(
            capabilityId,
            "request-content",
            context);

        Assert.Equal(
            2,
            authorizationPolicy.Requests.Count);

        Assert.Equal(
            ["artifact-001", "artifact-002"],
            authorizationPolicy.Requests
                .Select(request =>
                    request.ResourceId)
                .ToArray());

        Assert.All(
            authorizationPolicy.Requests,
            request =>
            {
                Assert.Equal(
                    SecurityPermissions
                        .ArtifactIntelligenceUse,
                    request.PermissionId);

                Assert.Equal(
                    context.SubjectId,
                    request.SubjectId);

                Assert.Equal(
                    context.ProtectionClassificationId,
                    request.ProtectionClassificationId);
            });
    }
}

using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task LocalStack_ExtractsAuthorizedKeywords()
    {
        var provider =
            new DevelopmentTextKeywordExtractionProvider();

        var authorizationPolicy =
            new RecordingAuthorizationPolicy();

        var auditSink =
            new RecordingAuditSink();

        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        provider.Id,
                        classificationId)
                ]);

        var executor =
            new IntelligenceCapabilityExecutor<
                TextKeywordExtractionRequest,
                IReadOnlyList<TextKeyword>>(
                new IntelligenceCapabilityProviderRouter<
                    TextKeywordExtractionRequest,
                    IReadOnlyList<TextKeyword>>(
                    [provider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "keyword-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ]);

        var result =
            await executor.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextKeywordExtraction,
                new TextKeywordExtractionRequest(
                    "Evidence evidence policy.",
                    2),
                context);

        Assert.True(result.Success);

        Assert.Equal(
            ["evidence", "policy"],
            result.Output!
                .Select(keyword => keyword.Term)
                .ToArray());

        Assert.Single(
            authorizationPolicy.Requests);

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            "IntelligenceCapability.Execute",
            audit.Operation);

        Assert.Equal(
            provider.ProviderId.Value,
            audit.Destination);

        Assert.Equal(
            context.CorrelationId.Value,
            audit.Facts["correlationId"]);
    }
}

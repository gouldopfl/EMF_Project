using EMF.Core.Models.Identities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;

namespace EMF.Tests;

public sealed partial class IntelligenceCapabilityExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesPermittedProvider()
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
                    new ArtifactId(
                        "artifact-001"),
                    new ArtifactId(
                        "artifact-002")
                ],
                new AgentId(
                    "analysis-agent"));

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        capabilityId,
                        context.ProtectionClassificationId)
                ]);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [provider],
                policy);

        var auditSink =
            new RecordingAuditSink();

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                    router,
                    new RecordingAuthorizationPolicy(),
                    auditSink);

        var result =
            await executor.ExecuteAsync(
                capabilityId,
                "request-content",
                context);

        Assert.Same(provider.Result, result);
        Assert.Equal(
            "request-content",
            provider.LastRequest);
        Assert.Same(context, provider.LastContext);

        var audit = Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Succeeded,
            audit.Outcome);
        Assert.Equal(
            AuthorizationDecision.Allow,
            audit.PolicyDecision);
        Assert.Equal(
            provider.ProviderId.Value,
            audit.Destination);
        Assert.Equal(
            capabilityId.Value,
            audit.ResourceId);
        Assert.Equal(
            "IntelligenceCapability.Execute",
            audit.Operation);
        Assert.Equal(
            "IntelligenceCapability",
            audit.ResourceType);
        Assert.Equal(
            context.SubjectId,
            audit.SubjectId);
        Assert.NotEqual(
            default,
            audit.OccurredUtc);
        Assert.Equal(
            context.CorrelationId.Value,
            audit.Facts["correlationId"]);
        Assert.Equal(
            context.ProtectionClassificationId.Value,
            audit.Facts[
                "protectionClassificationId"]);
        Assert.Equal(
            "artifact-001,artifact-002",
            audit.Facts["inputArtifactIds"]);
        Assert.Equal(
            "analysis-agent",
            audit.Facts["agentId"]);
        Assert.Equal(
            provider.Result.Metadata.EngineName,
            audit.Facts["engineName"]);
        Assert.Equal(
            provider.Result.Metadata.EngineVersion,
            audit.Facts["engineVersion"]);
        Assert.Equal(
            provider.Result.Metadata.ProviderOperationId,
            audit.Facts["providerOperationId"]);
        Assert.Equal(
            provider.Result.Metadata.StartedUtc.ToString("O"),
            audit.Facts["startedUtc"]);
        Assert.Equal(
            provider.Result.Metadata.CompletedUtc.ToString("O"),
            audit.Facts["completedUtc"]);
        Assert.Equal(
            9,
            audit.Facts.Count);
    }

    [Fact]
    public async Task
        ExecuteAsync_DoesNotFallbackAfterSelectedProviderFailure()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var failure =
            new InvalidOperationException(
                "Primary provider failed.");

        var primaryProvider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-primary"))
            {
                Failure = failure
            };

        var fallbackProvider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-fallback"));

        var context = CreateContext();

        var policy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                [
                    new IntelligenceProviderRoutingGrant(
                        primaryProvider.ProviderId,
                        capabilityId,
                        context.ProtectionClassificationId),
                    new IntelligenceProviderRoutingGrant(
                        fallbackProvider.ProviderId,
                        capabilityId,
                        context.ProtectionClassificationId)
                ]);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [
                    primaryProvider,
                    fallbackProvider
                ],
                policy);

        var auditSink =
            new RecordingAuditSink();

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                    router,
                    new RecordingAuthorizationPolicy(),
                    auditSink);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    context));

        Assert.Same(failure, exception);

        Assert.Null(
            fallbackProvider.LastRequest);
        Assert.Null(
            fallbackProvider.LastContext);

        var audit =
            Assert.Single(
                auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);
        Assert.Equal(
            primaryProvider.ProviderId.Value,
            audit.Destination);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenNoProviderPermitted()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var provider =
            new TestProvider(
                capabilityId,
                new IntelligenceProviderId(
                    "provider-one"));

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [provider],
                new ConfiguredIntelligenceProviderRoutingPolicy(
                    Array.Empty<
                        IntelligenceProviderRoutingGrant>()));

        var auditSink =
            new RecordingAuditSink();

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(
                    router,
                    new RecordingAuthorizationPolicy(),
                    auditSink);

        var exception =
            await Assert.ThrowsAsync<
                IntelligenceProviderUnavailableException>(
                () => executor.ExecuteAsync(
                    capabilityId,
                    "request-content",
                    CreateContext()));

        Assert.Equal(
            capabilityId,
            exception.CapabilityId);

        Assert.Null(provider.LastRequest);

        var audit = Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Denied,
            audit.Outcome);
        Assert.Equal(
            AuthorizationDecision.Deny,
            audit.PolicyDecision);
        Assert.Null(audit.Destination);
    }

    private static IntelligenceExecutionContext
        CreateContext()
    {
        return new IntelligenceExecutionContext(
            "security-steward",
            new IntelligenceCorrelationId(
                "operation-001"),
            new ProtectionClassificationId(
                "confidential"),
            []);
    }

    private sealed class TestProvider :
        IIntelligenceCapabilityProvider<
            string,
            string>
    {
        public TestProvider(
            IntelligenceCapabilityId id,
            IntelligenceProviderId providerId)
        {
            Id = id;
            ProviderId = providerId;

            Result =
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = "result-content",
                    Metadata =
                        new IntelligenceExecutionMetadata
                        {
                            CapabilityId = id,
                            ProviderId = providerId,
                            CorrelationId =
                                new IntelligenceCorrelationId(
                                    "operation-001"),
                            EngineName = "test-engine",
                            EngineVersion = "1.2.3",
                            ProviderOperationId =
                                "provider-operation-001",
                            StartedUtc =
                                DateTimeOffset.UtcNow,
                            CompletedUtc =
                                DateTimeOffset.UtcNow
                        }
                };
        }

        public IntelligenceCapabilityId Id { get; }

        public IntelligenceProviderId ProviderId
        {
            get;
        }

        public IntelligenceCapabilityResult<string>
            Result { get; set; }

        public Exception? Failure { get; init; }

        public string? LastRequest { get; private set; }

        public IntelligenceExecutionContext? LastContext
        {
            get;
            private set;
        }

        public Task<
            IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                string request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<
                    IntelligenceCapabilityResult<string>>(
                    Failure);
            }

            LastRequest = request;
            LastContext = context;

            return Task.FromResult(Result);
        }
    }
}

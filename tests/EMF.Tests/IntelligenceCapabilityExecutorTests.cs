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

        var context = CreateContext();

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

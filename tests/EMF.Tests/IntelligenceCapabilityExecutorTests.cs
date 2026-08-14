using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceCapabilityExecutorTests
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

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(router);

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

        var executor =
            new IntelligenceCapabilityExecutor<
                string,
                string>(router);

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
            Result { get; }

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

            LastRequest = request;
            LastContext = context;

            return Task.FromResult(Result);
        }
    }
}

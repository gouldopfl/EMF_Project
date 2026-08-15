using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceCapabilityProviderRouterTests
{
    [Fact]
    public void Constructor_RejectsNullProviders()
    {
        var policy = new RecordingRoutingPolicy();

        Assert.Throws<ArgumentNullException>(
            () => new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                null!,
                policy));
    }

    [Fact]
    public void Constructor_RejectsNullRoutingPolicy()
    {
        var providers =
            Array.Empty<
                IIntelligenceCapabilityProvider<
                    string,
                    string>>();

        Assert.Throws<ArgumentNullException>(
            () => new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                providers,
                null!));
    }

    [Fact]
    public void Constructor_RejectsNullProviderEntry()
    {
        IIntelligenceCapabilityProvider<
            string,
            string>[] providers =
        [
            null!
        ];

        Assert.Throws<ArgumentException>(
            () => new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                providers,
                new RecordingRoutingPolicy()));
    }

    [Fact]
    public async Task SelectAsync_ReturnsFirstPermittedProvider()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var denied =
            new TestProvider(
                capabilityId,
                "provider-denied");

        var permitted =
            new TestProvider(
                capabilityId,
                "provider-permitted");

        var policy =
            new RecordingRoutingPolicy(
                permitted.ProviderId);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [denied, permitted],
                policy);

        var selected =
            await router.SelectAsync(
                capabilityId,
                CreateContext());

        Assert.Same(permitted, selected);
        Assert.Equal(2, policy.Evaluated.Count);
    }

    [Fact]
    public async Task SelectAsync_SkipsUnrelatedCapability()
    {
        var requestedId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var unrelated =
            new TestProvider(
                new IntelligenceCapabilityId(
                    "image-analysis"),
                "unrelated-provider");

        var matching =
            new TestProvider(
                requestedId,
                "matching-provider");

        var policy =
            new RecordingRoutingPolicy(
                matching.ProviderId);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [unrelated, matching],
                policy);

        var selected =
            await router.SelectAsync(
                requestedId,
                CreateContext());

        Assert.Same(matching, selected);
        Assert.Single(policy.Evaluated);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNullWhenNoProviderMatchesCapability()
    {
        var requestedId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var unrelated =
            new TestProvider(
                new IntelligenceCapabilityId(
                    "image-analysis"),
                "image-provider");

        var policy =
            new RecordingRoutingPolicy(
                unrelated.ProviderId);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [unrelated],
                policy);

        var selected =
            await router.SelectAsync(
                requestedId,
                CreateContext());

        Assert.Null(selected);
        Assert.Empty(policy.Evaluated);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNullWhenAllMatchingProvidersAreDenied()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var first =
            new TestProvider(
                capabilityId,
                "provider-one");

        var second =
            new TestProvider(
                capabilityId,
                "provider-two");

        var policy =
            new RecordingRoutingPolicy();

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [first, second],
                policy);

        var selected =
            await router.SelectAsync(
                capabilityId,
                CreateContext());

        Assert.Null(selected);
        Assert.Equal(2, policy.Evaluated.Count);
        Assert.Equal(
            first.ProviderId,
            policy.Evaluated[0]);
        Assert.Equal(
            second.ProviderId,
            policy.Evaluated[1]);
    }

    [Fact]
    public async Task SelectAsync_ThrowsWhenCancellationIsRequested()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document-analysis");

        var provider =
            new TestProvider(
                capabilityId,
                "provider-one");

        var policy =
            new RecordingRoutingPolicy(
                provider.ProviderId);

        var router =
            new IntelligenceCapabilityProviderRouter<
                string,
                string>(
                [provider],
                policy);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => router.SelectAsync(
                capabilityId,
                CreateContext(),
                cancellationSource.Token));

        Assert.Empty(policy.Evaluated);
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
            string providerId)
        {
            Id = id;
            ProviderId =
                new IntelligenceProviderId(
                    providerId);
        }

        public IntelligenceCapabilityId Id { get; }

        public IntelligenceProviderId ProviderId
        {
            get;
        }

        public Task<
            IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                string request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingRoutingPolicy :
        IIntelligenceProviderRoutingPolicy
    {
        private readonly IntelligenceProviderId
            _permittedProviderId;

        public RecordingRoutingPolicy(
            IntelligenceProviderId
                permittedProviderId = default)
        {
            _permittedProviderId =
                permittedProviderId;
        }

        public List<IntelligenceProviderId> Evaluated
        {
            get;
        } = [];

        public Task<IntelligenceProviderRoutingDecision>
            EvaluateAsync(
                IntelligenceProviderId providerId,
                IntelligenceCapabilityId capabilityId,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            Evaluated.Add(providerId);

            return Task.FromResult(
                new IntelligenceProviderRoutingDecision
                {
                    Permitted =
                        providerId ==
                        _permittedProviderId
                });
        }
    }
}

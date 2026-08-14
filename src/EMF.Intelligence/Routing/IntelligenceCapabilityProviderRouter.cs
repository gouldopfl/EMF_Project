using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Routing;

public sealed class IntelligenceCapabilityProviderRouter<
    TRequest,
    TResult>
    where TRequest : notnull
    where TResult : notnull
{
    private readonly IReadOnlyList<
        IIntelligenceCapabilityProvider<
            TRequest,
            TResult>> _providers;

    private readonly IIntelligenceProviderRoutingPolicy
        _routingPolicy;

    public IntelligenceCapabilityProviderRouter(
        IEnumerable<
            IIntelligenceCapabilityProvider<
                TRequest,
                TResult>> providers,
        IIntelligenceProviderRoutingPolicy routingPolicy)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(routingPolicy);

        var configuredProviders = providers.ToArray();

        if (configuredProviders.Any(
                provider => provider is null))
        {
            throw new ArgumentException(
                "Configured intelligence providers cannot contain null.",
                nameof(providers));
        }

        _providers = configuredProviders;
        _routingPolicy = routingPolicy;
    }

    public async Task<
        IIntelligenceCapabilityProvider<
            TRequest,
            TResult>?> SelectAsync(
        IntelligenceCapabilityId capabilityId,
        IntelligenceExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            capabilityId.Value);

        ArgumentNullException.ThrowIfNull(context);

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (provider.Id != capabilityId)
            {
                continue;
            }

            var decision =
                await _routingPolicy.EvaluateAsync(
                    provider.ProviderId,
                    capabilityId,
                    context,
                    cancellationToken);

            if (decision.Permitted)
            {
                return provider;
            }
        }

        return null;
    }
}

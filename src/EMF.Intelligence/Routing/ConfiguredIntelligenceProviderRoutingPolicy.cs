using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Routing;

public sealed class
    ConfiguredIntelligenceProviderRoutingPolicy :
    IIntelligenceProviderRoutingPolicy
{
    private readonly IReadOnlySet<
        IntelligenceProviderRoutingGrant> _grants;

    public ConfiguredIntelligenceProviderRoutingPolicy(
        IEnumerable<
            IntelligenceProviderRoutingGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var configuredGrants = grants.ToArray();

        if (configuredGrants.Any(
                grant =>
                    string.IsNullOrWhiteSpace(
                        grant.ProviderId.Value) ||
                    string.IsNullOrWhiteSpace(
                        grant.CapabilityId.Value) ||
                    string.IsNullOrWhiteSpace(
                        grant.ProtectionClassificationId
                            .Value)))
        {
            throw new ArgumentException(
                "Routing grants cannot contain default identities.",
                nameof(grants));
        }

        _grants = configuredGrants.ToHashSet();
    }

    public Task<IntelligenceProviderRoutingDecision>
        EvaluateAsync(
            IntelligenceProviderId providerId,
            IntelligenceCapabilityId capabilityId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        var requestedGrant =
            new IntelligenceProviderRoutingGrant(
                providerId,
                capabilityId,
                context.ProtectionClassificationId);

        var permitted =
            _grants.Contains(requestedGrant);

        return Task.FromResult(
            new IntelligenceProviderRoutingDecision
            {
                Permitted = permitted,
                Reason = permitted
                    ? null
                    : "No configured routing grant permits disclosure."
            });
    }
}

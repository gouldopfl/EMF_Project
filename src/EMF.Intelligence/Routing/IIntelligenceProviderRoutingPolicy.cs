using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Routing;

public interface IIntelligenceProviderRoutingPolicy
{
    Task<IntelligenceProviderRoutingDecision>
        EvaluateAsync(
            IntelligenceProviderId providerId,
            IntelligenceCapabilityId capabilityId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default);
}

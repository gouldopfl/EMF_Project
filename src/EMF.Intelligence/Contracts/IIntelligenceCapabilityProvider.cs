using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Contracts;

public interface IIntelligenceCapabilityProvider<
    TRequest,
    TResult> :
    IIntelligenceCapability<TRequest, TResult>
    where TRequest : notnull
    where TResult : notnull
{
    IntelligenceProviderId ProviderId { get; }
}

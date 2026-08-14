using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Intelligence.Routing;

public readonly record struct
    IntelligenceProviderRoutingGrant
{
    public IntelligenceProviderRoutingGrant(
        IntelligenceProviderId providerId,
        IntelligenceCapabilityId capabilityId,
        ProtectionClassificationId
            protectionClassificationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerId.Value);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            capabilityId.Value);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            protectionClassificationId.Value);

        ProviderId = providerId;
        CapabilityId = capabilityId;
        ProtectionClassificationId =
            protectionClassificationId;
    }

    public IntelligenceProviderId ProviderId { get; }

    public IntelligenceCapabilityId CapabilityId { get; }

    public ProtectionClassificationId
        ProtectionClassificationId { get; }
}

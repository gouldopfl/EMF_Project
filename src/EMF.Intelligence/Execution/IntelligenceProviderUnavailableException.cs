using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Execution;

public sealed class
    IntelligenceProviderUnavailableException :
    InvalidOperationException
{
    public IntelligenceProviderUnavailableException(
        IntelligenceCapabilityId capabilityId)
        : base(
            "No configured and permitted intelligence " +
            $"provider is available for '{capabilityId.Value}'.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            capabilityId.Value);

        CapabilityId = capabilityId;
    }

    public IntelligenceCapabilityId CapabilityId
    {
        get;
    }
}

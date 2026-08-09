using EMF.Core.Contracts;

namespace EMF.Extensions.VeteransClaims;

public sealed class VeteransClaimsDomainExtension : IDomainExtension
{
    public string ComponentId => "emf.domain.veterans-claims";

    public string DisplayName => "Veterans Claims";

    public Version ComponentVersion => new(1, 0, 0);
}

using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class EffectiveDateRegulatoryProvision
{
    public required EffectiveDateId EffectiveDateId
    {
        get;
        init;
    }

    public required RegulatoryProvisionId RegulatoryProvisionId
    {
        get;
        init;
    }
}

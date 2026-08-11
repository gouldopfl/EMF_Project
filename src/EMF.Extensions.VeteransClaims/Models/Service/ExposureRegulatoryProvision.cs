using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ExposureRegulatoryProvision
{
    public required ExposureId ExposureId { get; init; }

    public required RegulatoryProvisionId RegulatoryProvisionId
    {
        get;
        init;
    }
}

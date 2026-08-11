using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ExposureRequirement
{
    public required ExposureId ExposureId { get; init; }

    public required RequirementId RequirementId { get; init; }
}

using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasisRequirement
{
    public required ServiceConnectionBasisId
        ServiceConnectionBasisId { get; init; }

    public required RequirementId
        RequirementId { get; init; }
}

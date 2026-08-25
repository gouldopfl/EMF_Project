using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisRequirementDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required Requirement Requirement { get; init; }
}

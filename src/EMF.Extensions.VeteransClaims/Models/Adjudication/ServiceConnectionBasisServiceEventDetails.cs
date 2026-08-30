using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ServiceConnectionBasisServiceEventDetails
{
    public required ServiceConnectionBasis Basis { get; init; }

    public required ServiceEvent ServiceEvent { get; init; }
}

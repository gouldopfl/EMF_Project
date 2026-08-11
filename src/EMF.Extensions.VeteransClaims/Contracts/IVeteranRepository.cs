using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IVeteranRepository
{
    Task AddVeteranAsync(
        Veteran veteran,
        CancellationToken cancellationToken = default);

    Task<Veteran?> GetVeteranAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default);
}

using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IRegulatoryRepository
{
    Task AddRegulatoryAuthorityAsync(
        RegulatoryAuthority authority,
        CancellationToken cancellationToken = default);

    Task<RegulatoryAuthority?> GetRegulatoryAuthorityAsync(
        RegulatoryAuthorityId authorityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegulatoryAuthority>>
        GetRegulatoryAuthoritiesAsync(
            CancellationToken cancellationToken = default);

    Task AddRegulatoryProvisionAsync(
        RegulatoryProvision provision,
        CancellationToken cancellationToken = default);

    Task<RegulatoryProvision?> GetRegulatoryProvisionAsync(
        RegulatoryProvisionId provisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegulatoryProvision>>
        GetRegulatoryProvisionsAsync(
            RegulatoryAuthorityId authorityId,
            CancellationToken cancellationToken = default);

    Task AddRequirementAsync(
        Requirement requirement,
        CancellationToken cancellationToken = default);

    Task<Requirement?> GetRequirementAsync(
        RequirementId requirementId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Requirement>> GetRequirementsAsync(
        RegulatoryProvisionId provisionId,
        CancellationToken cancellationToken = default);
}

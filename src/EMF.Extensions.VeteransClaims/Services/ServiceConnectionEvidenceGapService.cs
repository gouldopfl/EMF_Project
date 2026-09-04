using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ServiceConnectionEvidenceGapService :
    IServiceConnectionEvidenceGapService
{
    private readonly IServiceConnectionRepository _serviceConnections;
    private readonly IEvidenceGapService _gaps;

    public ServiceConnectionEvidenceGapService(
        IServiceConnectionRepository serviceConnections,
        IEvidenceGapService gaps)
    {
        ArgumentNullException.ThrowIfNull(serviceConnections);
        ArgumentNullException.ThrowIfNull(gaps);

        _serviceConnections = serviceConnections;
        _gaps = gaps;
    }

    public async Task<IReadOnlyList<EvidenceGap>> EnsureGapsAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        var bases =
            await _serviceConnections.GetServiceConnectionBasesAsync(
                claimIssueId,
                cancellationToken);

        if (bases.Any(x => x.ClaimIssueId != claimIssueId))
            throw new InvalidOperationException(
                "Service connection basis claim issue mismatch.");

        var requirementIds = new HashSet<RequirementId>();

        foreach (var basis in bases)
        {
            var ids =
                await _serviceConnections.GetRequirementIdsAsync(
                    basis.Id,
                    cancellationToken);

            foreach (var id in ids)
                requirementIds.Add(id);
        }

        var gaps = new List<EvidenceGap>();

        foreach (var requirementId in requirementIds)
        {
            var gap =
                await _gaps.EnsureGapAsync(
                    claimIssueId,
                    requirementId,
                    cancellationToken);

            if (gap is not null)
                gaps.Add(gap);
        }

        return gaps;
    }
}

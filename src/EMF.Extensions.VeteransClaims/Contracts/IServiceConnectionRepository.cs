using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IServiceConnectionRepository
{
    Task AddServiceConnectionTheoryAsync(
        ServiceConnectionTheory theory,
        CancellationToken cancellationToken = default);

    Task<ServiceConnectionTheory?>
        GetServiceConnectionTheoryAsync(
            ServiceConnectionTheoryId theoryId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionTheory>>
        GetServiceConnectionTheoriesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);
    Task AddServiceConnectionBasisAsync(
        ServiceConnectionBasis basis,
        CancellationToken cancellationToken = default);

    Task<ServiceConnectionBasis?>
        GetServiceConnectionBasisAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasis>>
        GetServiceConnectionBasesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasis>>
        GetServiceConnectionBasesAsync(
            ServiceConnectionTheoryId theoryId,
            CancellationToken cancellationToken = default);

    Task AddBasisClaimedConditionAsync(
        ServiceConnectionBasisClaimedCondition association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimedConditionId>>
        GetClaimedConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default);

    Task AddBasisServiceEventAsync(
        ServiceConnectionBasisServiceEvent association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceEventId>>
        GetServiceEventIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ServiceEventId serviceEventId,
            CancellationToken cancellationToken = default);

}

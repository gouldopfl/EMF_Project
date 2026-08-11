using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IConditionRepository
{
    Task AddClaimedConditionAsync(
        ClaimedCondition claimedCondition,
        CancellationToken cancellationToken = default);

    Task<ClaimedCondition?> GetClaimedConditionAsync(
        ClaimedConditionId claimedConditionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimedCondition>>
        GetClaimedConditionsAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default);

    Task AddMedicalConditionAsync(
        MedicalCondition medicalCondition,
        CancellationToken cancellationToken = default);

    Task<MedicalCondition?> GetMedicalConditionAsync(
        MedicalConditionId medicalConditionId,
        CancellationToken cancellationToken = default);

    Task AddVeteranMedicalConditionAsync(
        VeteranMedicalCondition association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalConditionId>>
        GetMedicalConditionIdsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VeteranId>> GetVeteranIdsAsync(
        MedicalConditionId medicalConditionId,
        CancellationToken cancellationToken = default);

    Task AddClaimedConditionMedicalConditionAsync(
        ClaimedConditionMedicalCondition association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalConditionId>>
        GetMedicalConditionIdsAsync(
            ClaimedConditionId claimedConditionId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimedConditionId>>
        GetClaimedConditionIdsAsync(
            MedicalConditionId medicalConditionId,
            CancellationToken cancellationToken = default);
}

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

    Task AddBasisExposureAsync(
        ServiceConnectionBasisExposure association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExposureId>>
        GetExposureIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectionBasisIdsAsync(
            ExposureId exposureId,
            CancellationToken cancellationToken = default);

    Task AddBasisServiceConnectedConditionAsync(
        ServiceConnectionBasisServiceConnectedCondition association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalConditionId>>
        GetServiceConnectedConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetServiceConnectedConditionBasisIdsAsync(
            MedicalConditionId serviceConnectedConditionId,
            CancellationToken cancellationToken = default);


    Task AddBasisPrescribedMedicationAsync(
        ServiceConnectionBasisPrescribedMedication association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>>
        GetPrescribedMedicationNamesAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetPrescribedMedicationBasisIdsAsync(
            string medicationName,
            CancellationToken cancellationToken = default);

    Task AddBasisPresumptionAsync(
        ServiceConnectionBasisPresumption association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegulatoryProvisionId>>
        GetPresumptionProvisionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetPresumptionBasisIdsAsync(
            RegulatoryProvisionId presumptionProvisionId,
            CancellationToken cancellationToken = default);

    Task AddBasisPreexistingConditionAsync(
        ServiceConnectionBasisPreexistingCondition association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalConditionId>>
        GetPreexistingConditionIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetPreexistingConditionBasisIdsAsync(
            MedicalConditionId preexistingConditionId,
            CancellationToken cancellationToken = default);

    Task AddBasisMedicalOpinionAsync(
        ServiceConnectionBasisMedicalOpinion association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisMedicalOpinion>>
        GetBasisMedicalOpinionsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);


    Task AddBasisRequirementAsync(
        ServiceConnectionBasisRequirement association,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RequirementId>>
        GetRequirementIdsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceConnectionBasisId>>
        GetRequirementBasisIdsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default);

}

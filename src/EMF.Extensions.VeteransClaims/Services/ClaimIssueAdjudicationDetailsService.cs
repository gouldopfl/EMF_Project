using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationDetailsService :
    IClaimIssueAdjudicationDetailsService
{
    private readonly IClaimIssueRepository _issues;
    private readonly IConditionRepository _conditions;
    private readonly IServiceConnectionRepository _serviceConnections;
    private readonly IServiceHistoryRepository _serviceHistory;
    private readonly IRegulatoryRepository _regulatory;
    private readonly IRequirementEvidenceService _requirementEvidence;
    private readonly IClaimIssueEvidenceDetailsService _evidence;
    private readonly IClaimIssueAdjudicationTimelineService _timeline;

    public ClaimIssueAdjudicationDetailsService(
        IClaimIssueRepository issues,
        IConditionRepository conditions,
        IServiceConnectionRepository serviceConnections,
        IServiceHistoryRepository serviceHistory,
        IRegulatoryRepository regulatory,
        IRequirementEvidenceService requirementEvidence,
        IClaimIssueEvidenceDetailsService evidence,
        IClaimIssueAdjudicationTimelineService timeline)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(serviceConnections);
        ArgumentNullException.ThrowIfNull(serviceHistory);
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(requirementEvidence);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(timeline);

        _issues = issues;
        _conditions = conditions;
        _serviceConnections = serviceConnections;
        _serviceHistory = serviceHistory;
        _regulatory = regulatory;
        _requirementEvidence = requirementEvidence;
        _evidence = evidence;
        _timeline = timeline;
    }

    public async Task<ClaimIssueAdjudicationDetails?>
        GetAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        var issue =
            await _issues.GetClaimIssueAsync(
                claimIssueId,
                cancellationToken);

        if (issue is null)
            return null;

        if (issue.Id != claimIssueId)
            throw new InvalidOperationException(
                "Claim issue lookup returned a different issue.");

        var claimedConditions =
            await _conditions.GetClaimedConditionsAsync(
                claimIssueId,
                cancellationToken);

        if (claimedConditions.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Claimed condition claim issue mismatch.");
        }

        var theories =
            await _serviceConnections
                .GetServiceConnectionTheoriesAsync(
                    claimIssueId,
                    cancellationToken);

        if (theories.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Service-connection theory claim issue mismatch.");
        }

        var bases =
            await _serviceConnections
                .GetServiceConnectionBasesAsync(
                    claimIssueId,
                    cancellationToken);

        if (bases.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Service-connection basis claim issue mismatch.");
        }

        if (bases.Any(
            basis =>
                theories.All(
                    theory =>
                        theory.Id !=
                        basis.ServiceConnectionTheoryId)))
        {
            throw new InvalidOperationException(
                "Service-connection basis theory mismatch.");
        }

        var serviceConnectedConditions =
            new List<ServiceConnectionBasisConditionDetails>();

        var prescribedMedications =
            new List<ServiceConnectionBasisMedicationDetails>();

        var exposures =
            new List<ServiceConnectionBasisExposureDetails>();

        var serviceEvents =
            new List<ServiceConnectionBasisServiceEventDetails>();

        var requirements =
            new List<ServiceConnectionBasisRequirementDetails>();

        foreach (var basis in bases)
        {
            var conditionIds =
                await _serviceConnections
                    .GetServiceConnectedConditionIdsAsync(
                        basis.Id,
                        cancellationToken);

            foreach (var conditionId in conditionIds)
            {
                var condition =
                    await _conditions.GetMedicalConditionAsync(
                        conditionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Service-connected condition could not be read.");

                if (condition.Id != conditionId)
                    throw new InvalidOperationException(
                        "Service-connected condition identity mismatch.");

                serviceConnectedConditions.Add(
                    new ServiceConnectionBasisConditionDetails
                    {
                        Basis = basis,
                        ServiceConnectedCondition = condition
                    });
            }

            var medicationNames =
                await _serviceConnections
                    .GetPrescribedMedicationNamesAsync(
                        basis.Id,
                        cancellationToken);

            foreach (var medicationName in medicationNames)
            {
                prescribedMedications.Add(
                    new ServiceConnectionBasisMedicationDetails
                    {
                        Basis = basis,
                        MedicationName = medicationName
                    });
            }

            var exposureIds =
                await _serviceConnections.GetExposureIdsAsync(
                    basis.Id,
                    cancellationToken);

            foreach (var exposureId in exposureIds)
            {
                var exposure =
                    await _serviceHistory.GetExposureAsync(
                        exposureId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Exposure could not be read.");

                if (exposure.Id != exposureId)
                    throw new InvalidOperationException(
                        "Exposure identity mismatch.");

                exposures.Add(
                    new ServiceConnectionBasisExposureDetails
                    {
                        Basis = basis,
                        Exposure = exposure
                    });
            }

            var serviceEventIds =
                await _serviceConnections.GetServiceEventIdsAsync(
                    basis.Id,
                    cancellationToken);

            foreach (var serviceEventId in serviceEventIds)
            {
                var serviceEvent =
                    await _serviceHistory.GetServiceEventAsync(
                        serviceEventId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Service event could not be read.");

                if (serviceEvent.Id != serviceEventId)
                    throw new InvalidOperationException(
                        "Service event identity mismatch.");

                serviceEvents.Add(
                    new ServiceConnectionBasisServiceEventDetails
                    {
                        Basis = basis,
                        ServiceEvent = serviceEvent
                    });
            }

            var requirementIds =
                await _serviceConnections.GetRequirementIdsAsync(
                    basis.Id,
                    cancellationToken);

            foreach (var requirementId in requirementIds)
            {
                var requirement =
                    await _regulatory.GetRequirementAsync(
                        requirementId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Service-connection requirement could not be read.");

                if (requirement.Id != requirementId)
                    throw new InvalidOperationException(
                        "Service-connection requirement identity mismatch.");

                var provision =
                    await _regulatory.GetRegulatoryProvisionAsync(
                        requirement.RegulatoryProvisionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Regulatory provision could not be read.");

                if (provision.Id != requirement.RegulatoryProvisionId)
                    throw new InvalidOperationException(
                        "Regulatory provision identity mismatch.");

                var responsiveness =
                    await _requirementEvidence
                        .AssessResponsivenessAsync(
                            requirement.Id,
                            cancellationToken);

                var developmentChecklist =
                    await _requirementEvidence
                        .CreateChecklistAsync(
                            requirement.Id,
                            cancellationToken);

                requirements.Add(
                    new ServiceConnectionBasisRequirementDetails
                    {
                        Basis = basis,
                        Requirement = requirement,
                        RegulatoryProvision = provision,
                        Responsiveness = responsiveness,
                        DevelopmentChecklist = developmentChecklist
                    });
            }
        }

        var evidence =
            await _evidence.GetAsync(
                claimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Claim issue evidence details could not be read.");

        var timeline =
            await _timeline.GetAsync(
                claimIssueId,
                cancellationToken);

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = claimedConditions,
            ServiceConnectionTheories = theories,
            ServiceConnectionBases = bases,
            ServiceConnectedConditions = serviceConnectedConditions,
            PrescribedMedications = prescribedMedications,
            Exposures = exposures,
            ServiceEvents = serviceEvents,
            Requirements = requirements,
            Evidence = evidence,
            Timeline = timeline
        };
    }
}

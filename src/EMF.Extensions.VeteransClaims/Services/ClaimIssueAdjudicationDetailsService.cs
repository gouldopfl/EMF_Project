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

        var claimedConditions =
            await _conditions.GetClaimedConditionsAsync(
                claimIssueId,
                cancellationToken);

        var theories =
            await _serviceConnections
                .GetServiceConnectionTheoriesAsync(
                    claimIssueId,
                    cancellationToken);

        var bases =
            await _serviceConnections
                .GetServiceConnectionBasesAsync(
                    claimIssueId,
                    cancellationToken);

        var serviceConnectedConditions =
            new List<ServiceConnectionBasisConditionDetails>();

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

                serviceConnectedConditions.Add(
                    new ServiceConnectionBasisConditionDetails
                    {
                        Basis = basis,
                        ServiceConnectedCondition = condition
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

                var provision =
                    await _regulatory.GetRegulatoryProvisionAsync(
                        requirement.RegulatoryProvisionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Regulatory provision could not be read.");

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
            ServiceEvents = serviceEvents,
            Requirements = requirements,
            Evidence = evidence,
            Timeline = timeline
        };
    }
}

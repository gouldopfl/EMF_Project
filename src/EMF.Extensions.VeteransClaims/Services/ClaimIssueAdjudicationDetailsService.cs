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
    private readonly IRegulatoryRepository _regulatory;
    private readonly IClaimIssueEvidenceDetailsService _evidence;

    public ClaimIssueAdjudicationDetailsService(
        IClaimIssueRepository issues,
        IConditionRepository conditions,
        IServiceConnectionRepository serviceConnections,
        IRegulatoryRepository regulatory,
        IClaimIssueEvidenceDetailsService evidence)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(serviceConnections);
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(evidence);

        _issues = issues;
        _conditions = conditions;
        _serviceConnections = serviceConnections;
        _regulatory = regulatory;
        _evidence = evidence;
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

                requirements.Add(
                    new ServiceConnectionBasisRequirementDetails
                    {
                        Basis = basis,
                        Requirement = requirement
                    });
            }
        }

        var evidence =
            await _evidence.GetAsync(
                claimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Claim issue evidence details could not be read.");

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = claimedConditions,
            ServiceConnectionTheories = theories,
            ServiceConnectionBases = bases,
            ServiceConnectedConditions = serviceConnectedConditions,
            Requirements = requirements,
            Evidence = evidence
        };
    }
}

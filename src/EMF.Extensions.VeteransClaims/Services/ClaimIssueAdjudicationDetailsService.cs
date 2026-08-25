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
    private readonly IClaimIssueEvidenceDetailsService _evidence;

    public ClaimIssueAdjudicationDetailsService(
        IClaimIssueRepository issues,
        IConditionRepository conditions,
        IServiceConnectionRepository serviceConnections,
        IClaimIssueEvidenceDetailsService evidence)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(serviceConnections);
        ArgumentNullException.ThrowIfNull(evidence);

        _issues = issues;
        _conditions = conditions;
        _serviceConnections = serviceConnections;
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
            Evidence = evidence
        };
    }
}

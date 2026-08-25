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
            Evidence = evidence
        };
    }
}

using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationAgingStatusService
{
    private readonly ClaimIssueAdjudicationAgingService _aging;
    private readonly ClaimIssueAdjudicationAgingPolicyService _policy;

    public ClaimIssueAdjudicationAgingStatusService(
        ClaimIssueAdjudicationAgingService aging,
        ClaimIssueAdjudicationAgingPolicyService policy)
    {
        _aging = aging;
        _policy = policy;
    }

    public ClaimIssueAdjudicationAgingStatus? TryAssess(
        ClaimIssueId claimIssueId,
        IReadOnlyCollection<ClaimIssueAdjudicationEvent> timeline,
        DateTimeOffset asOf,
        ClaimIssueAdjudicationAgingPolicy policy)
    {
        var aging =
            _aging.TryAssess(
                claimIssueId,
                timeline,
                asOf);

        if (aging is null)
            return null;

        var alertLevel =
            _policy.Evaluate(
                aging,
                policy);

        return new ClaimIssueAdjudicationAgingStatus
        {
            Aging = aging,
            AlertLevel = alertLevel
        };
    }

    public ClaimIssueAdjudicationAgingStatus Assess(
        ClaimIssueId claimIssueId,
        IReadOnlyCollection<ClaimIssueAdjudicationEvent> timeline,
        DateTimeOffset asOf,
        ClaimIssueAdjudicationAgingPolicy policy)
    {
        var aging =
            _aging.Assess(
                claimIssueId,
                timeline,
                asOf);

        var alertLevel =
            _policy.Evaluate(
                aging,
                policy);

        return new ClaimIssueAdjudicationAgingStatus
        {
            Aging = aging,
            AlertLevel = alertLevel
        };
    }
}

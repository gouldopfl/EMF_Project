using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceDevelopmentPlanStatusService
{
    public EvidenceDevelopmentPlanStatus Assess(
        EvidenceDevelopmentPlanDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var linkedGapIds =
            details.EvidenceGaps
                .Select(x => x.EvidenceGapId)
                .ToArray();

        var known =
            details.GapDetails
                .Where(x => linkedGapIds.Contains(x.Id))
                .ToArray();

        string status;

        if (known.Length != linkedGapIds.Length)
        {
            status = EvidenceDevelopmentPlanStatuses.Unknown;
        }
        else if (known.Any(
            x => x.Status == EvidenceGapStatuses.Open))
        {
            status =
                EvidenceDevelopmentPlanStatuses.RequiresDevelopment;
        }
        else if (known.All(
            x => x.Status == EvidenceGapStatuses.Resolved))
        {
            status = EvidenceDevelopmentPlanStatuses.Complete;
        }
        else
        {
            status = EvidenceDevelopmentPlanStatuses.Unknown;
        }

        return new EvidenceDevelopmentPlanStatus
        {
            PlanId = details.Plan.Id,
            Status = status
        };
    }
}

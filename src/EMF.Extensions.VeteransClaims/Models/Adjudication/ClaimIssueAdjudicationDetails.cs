using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueAdjudicationDetails
{
    public required ClaimIssue ClaimIssue { get; init; }

    public required IReadOnlyList<ClaimedCondition>
        ClaimedConditions { get; init; }

    public required IReadOnlyList<ServiceConnectionTheory>
        ServiceConnectionTheories { get; init; }

    public required IReadOnlyList<ServiceConnectionBasis>
        ServiceConnectionBases { get; init; }

    public required IReadOnlyList<ServiceConnectionBasisConditionDetails>
        ServiceConnectedConditions { get; init; }

    public required ClaimIssueEvidenceDetails Evidence { get; init; }
}

using EMF.Extensions.VeteransClaims.Models.Claims;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimIssueEvidenceDetails
{
    public required ClaimIssue ClaimIssue { get; init; }

    public required ClaimIssueEvidenceChecklist Checklist { get; init; }

    public required IReadOnlyList<EvidenceDevelopmentPlan>
        DevelopmentPlans { get; init; }
}

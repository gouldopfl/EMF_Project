using EMF.Extensions.VeteransClaims.Models.Claims;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ClaimEvidenceDetails
{
    public required Claim Claim { get; init; }

    public required IReadOnlyList<ClaimIssueEvidenceDetails>
        Issues { get; init; }
}

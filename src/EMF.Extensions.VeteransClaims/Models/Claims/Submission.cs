using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Claims;

public sealed class Submission
{
    public required SubmissionId Id { get; init; }

    public required ClaimId ClaimId { get; init; }
}

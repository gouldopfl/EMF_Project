using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class MedicalOpinion
{
    public required MedicalOpinionId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string Question { get; init; }

    public required string Opinion { get; init; }
}

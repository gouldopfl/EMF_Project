using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionTheory
{
    public required ServiceConnectionTheoryId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required string TheoryType { get; init; }
}

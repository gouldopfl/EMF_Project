using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ServiceConnectionBasis
{
    public required ServiceConnectionBasisId Id { get; init; }

    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ServiceConnectionTheoryId ServiceConnectionTheoryId { get; init; }
}

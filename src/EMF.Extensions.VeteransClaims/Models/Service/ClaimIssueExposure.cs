using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Service;

public sealed class ClaimIssueExposure
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ExposureId ExposureId { get; init; }
}

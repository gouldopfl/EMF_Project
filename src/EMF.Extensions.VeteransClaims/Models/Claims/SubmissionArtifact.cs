using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Claims;

public sealed class SubmissionArtifact
{
    public required SubmissionId SubmissionId { get; init; }

    public required ArtifactId ArtifactId { get; init; }
}

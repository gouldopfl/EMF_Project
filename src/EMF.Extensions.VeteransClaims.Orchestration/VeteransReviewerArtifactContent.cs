using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerArtifactContent
{
    public required Artifact Artifact
    { get; init; }

    public required string Text
    { get; init; }
}

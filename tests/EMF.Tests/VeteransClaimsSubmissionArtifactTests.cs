using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsSubmissionArtifactTests
{
    [Fact]
    public void Association_PreservesSubmissionAndArtifact()
    {
        var submissionId =
            new SubmissionId("submission-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new SubmissionArtifact
            {
                SubmissionId = submissionId,
                ArtifactId = artifactId
            };

        Assert.Equal(
            submissionId,
            association.SubmissionId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);
    }
}

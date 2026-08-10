using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsIssueDecisionArtifactTests
{
    [Fact]
    public void Association_PreservesIssueDecisionAndArtifact()
    {
        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var artifactId =
            new ArtifactId("artifact-001");

        var association =
            new IssueDecisionArtifact
            {
                IssueDecisionId = issueDecisionId,
                ArtifactId = artifactId
            };

        Assert.Equal(
            issueDecisionId,
            association.IssueDecisionId);

        Assert.Equal(
            artifactId,
            association.ArtifactId);
    }
}

using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsIssueDecisionFindingTests
{
    [Fact]
    public void IssueDecisionFinding_LinksDecisionToFinding()
    {
        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var findingId =
            new FindingId("finding-001");

        var reference = new IssueDecisionFinding
        {
            IssueDecisionId = issueDecisionId,
            FindingId = findingId
        };

        Assert.Equal(
            issueDecisionId,
            reference.IssueDecisionId);

        Assert.Equal(
            findingId,
            reference.FindingId);
    }
}

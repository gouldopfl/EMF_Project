using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsIssueDecisionRegulatoryProvisionTests
{
    [Fact]
    public void Association_PreservesIssueDecisionAndRegulatoryProvision()
    {
        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var regulatoryProvisionId =
            new RegulatoryProvisionId("provision-001");

        var association =
            new IssueDecisionRegulatoryProvision
            {
                IssueDecisionId = issueDecisionId,
                RegulatoryProvisionId =
                    regulatoryProvisionId
            };

        Assert.Equal(
            issueDecisionId,
            association.IssueDecisionId);

        Assert.Equal(
            regulatoryProvisionId,
            association.RegulatoryProvisionId);
    }
}

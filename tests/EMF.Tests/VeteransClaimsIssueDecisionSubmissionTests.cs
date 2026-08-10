using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsIssueDecisionSubmissionTests
{
    [Fact]
    public void Association_PreservesIssueDecisionAndSubmission()
    {
        var issueDecisionId =
            new IssueDecisionId("issue-decision-001");

        var submissionId =
            new SubmissionId("submission-001");

        var association =
            new IssueDecisionSubmission
            {
                IssueDecisionId = issueDecisionId,
                SubmissionId = submissionId
            };

        Assert.Equal(
            issueDecisionId,
            association.IssueDecisionId);

        Assert.Equal(
            submissionId,
            association.SubmissionId);
    }
}

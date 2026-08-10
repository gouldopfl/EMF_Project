using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsModelTests
{
    [Fact]
    public void Veteran_PreservesIdentity()
    {
        var id = new VeteranId("veteran-001");

        var veteran = new Veteran
        {
            Id = id
        };

        Assert.Equal(id, veteran.Id);
    }

    [Fact]
    public void Claim_PreservesIdentityAndVeteranRelationship()
    {
        var claimId = new ClaimId("claim-001");
        var veteranId = new VeteranId("veteran-001");

        var claim = new Claim
        {
            Id = claimId,
            VeteranId = veteranId
        };

        Assert.Equal(claimId, claim.Id);
        Assert.Equal(veteranId, claim.VeteranId);
    }

    [Fact]
    public void ClaimIssue_PreservesIdentityAndClaimRelationship()
    {
        var issueId = new ClaimIssueId("claim-issue-001");
        var claimId = new ClaimId("claim-001");

        var issue = new ClaimIssue
        {
            Id = issueId,
            ClaimId = claimId,
            ClaimIssueType = ClaimIssueTypes.ServiceConnection
        };

        Assert.Equal(issueId, issue.Id);
        Assert.Equal(claimId, issue.ClaimId);
        Assert.Equal(
            ClaimIssueTypes.ServiceConnection,
            issue.ClaimIssueType);
    }

    [Fact]
    public void Submission_PreservesIdentityAndClaimRelationship()
    {
        var submissionId = new SubmissionId("submission-001");
        var claimId = new ClaimId("claim-001");

        var submission = new Submission
        {
            Id = submissionId,
            ClaimId = claimId,
            SubmissionType = SubmissionTypes.InitialClaim
        };

        Assert.Equal(submissionId, submission.Id);
        Assert.Equal(claimId, submission.ClaimId);
        Assert.Equal(
            SubmissionTypes.InitialClaim,
            submission.SubmissionType);
    }
}

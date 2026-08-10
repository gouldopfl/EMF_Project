using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Tests;

public sealed class VeteransClaimsServiceConnectionTests
{
    [Fact]
    public void ServiceConnectionTheory_PreservesIdentityIssueAndType()
    {
        var theoryId =
            new ServiceConnectionTheoryId("theory-001");

        var issueId =
            new ClaimIssueId("claim-issue-001");

        var theory = new ServiceConnectionTheory
        {
            Id = theoryId,
            ClaimIssueId = issueId,
            TheoryType = ServiceConnectionTheoryTypes.Secondary
        };

        Assert.Equal(theoryId, theory.Id);
        Assert.Equal(issueId, theory.ClaimIssueId);
        Assert.Equal(
            ServiceConnectionTheoryTypes.Secondary,
            theory.TheoryType);
    }

    [Fact]
    public void ServiceConnectionBasis_PreservesTheoryRelationship()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-001");

        var issueId =
            new ClaimIssueId("claim-issue-001");

        var theoryId =
            new ServiceConnectionTheoryId("theory-001");

        var basis = new ServiceConnectionBasis
        {
            Id = basisId,
            ClaimIssueId = issueId,
            ServiceConnectionTheoryId = theoryId
        };

        Assert.Equal(basisId, basis.Id);
        Assert.Equal(issueId, basis.ClaimIssueId);
        Assert.Equal(
            theoryId,
            basis.ServiceConnectionTheoryId);
    }
}

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

    [Fact]
    public void SecondaryBasis_PreservesServiceConnectedConditionRelationship()
    {
        var basisId =
            new ServiceConnectionBasisId("basis-secondary-001");

        var conditionId =
            new MedicalConditionId("ptsd-001");

        var association =
            new ServiceConnectionBasisServiceConnectedCondition
            {
                ServiceConnectionBasisId = basisId,
                ServiceConnectedConditionId = conditionId
            };

        Assert.Equal(
            basisId,
            association.ServiceConnectionBasisId);

        Assert.Equal(
            conditionId,
            association.ServiceConnectedConditionId);
    }
}

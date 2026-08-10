using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsRegulatoryTests
{
    [Fact]
    public void RegulatoryAuthority_PreservesIdentityAndCitation()
    {
        var authorityId =
            new RegulatoryAuthorityId("authority-001");

        var authority = new RegulatoryAuthority
        {
            Id = authorityId,
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Pensions, Bonuses, and Veterans Relief"
        };

        Assert.Equal(authorityId, authority.Id);
        Assert.Equal("Regulation", authority.AuthorityType);
        Assert.Equal("38 CFR", authority.Citation);
    }

    [Fact]
    public void RegulatoryProvision_PreservesAuthorityRelationship()
    {
        var provisionId =
            new RegulatoryProvisionId("provision-001");

        var authorityId =
            new RegulatoryAuthorityId("authority-001");

        var provision = new RegulatoryProvision
        {
            Id = provisionId,
            RegulatoryAuthorityId = authorityId,
            ProvisionType = RegulatoryProvisionTypes.Requirement,
            Citation = "38 CFR 3.303"
        };

        Assert.Equal(provisionId, provision.Id);
        Assert.Equal(
            authorityId,
            provision.RegulatoryAuthorityId);

        Assert.Equal(
            RegulatoryProvisionTypes.Requirement,
            provision.ProvisionType);
    }

    [Fact]
    public void Requirement_PreservesProvisionRelationship()
    {
        var requirementId =
            new RequirementId("requirement-001");

        var provisionId =
            new RegulatoryProvisionId("provision-001");

        var requirement = new Requirement
        {
            Id = requirementId,
            RegulatoryProvisionId = provisionId,
            Description = "Applicable adjudicative requirement"
        };

        Assert.Equal(requirementId, requirement.Id);
        Assert.Equal(
            provisionId,
            requirement.RegulatoryProvisionId);
    }
}

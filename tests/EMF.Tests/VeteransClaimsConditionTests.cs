using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsConditionTests
{
    [Fact]
    public void ClaimedCondition_PreservesIdentityIssueAndName()
    {
        var conditionId =
            new ClaimedConditionId("claimed-condition-001");

        var issueId =
            new ClaimIssueId("claim-issue-001");

        var condition = new ClaimedCondition
        {
            Id = conditionId,
            ClaimIssueId = issueId,
            Name = "Sleep Apnea"
        };

        Assert.Equal(conditionId, condition.Id);
        Assert.Equal(issueId, condition.ClaimIssueId);
        Assert.Equal("Sleep Apnea", condition.Name);
    }

    [Fact]
    public void MedicalCondition_PreservesIdentityAndName()
    {
        var conditionId =
            new MedicalConditionId("medical-condition-001");

        var condition = new MedicalCondition
        {
            Id = conditionId,
            Name = "Obstructive Sleep Apnea"
        };

        Assert.Equal(conditionId, condition.Id);
        Assert.Equal("Obstructive Sleep Apnea", condition.Name);
    }

    [Fact]
    public void ClaimedConditionMedicalCondition_PreservesAssociation()
    {
        var claimedConditionId =
            new ClaimedConditionId("claimed-condition-001");

        var medicalConditionId =
            new MedicalConditionId("medical-condition-001");

        var association =
            new ClaimedConditionMedicalCondition
            {
                ClaimedConditionId = claimedConditionId,
                MedicalConditionId = medicalConditionId
            };

        Assert.Equal(
            claimedConditionId,
            association.ClaimedConditionId);

        Assert.Equal(
            medicalConditionId,
            association.MedicalConditionId);
    }
}

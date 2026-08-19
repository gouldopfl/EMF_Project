using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsEvidenceRequirementGuidanceTests
{
    [Fact]
    public void Guidance_ExposesRequiredDomainProperties()
    {
        var guidance =
            new EvidenceRequirementGuidance
            {
                Id =
                    new EvidenceRequirementGuidanceId(
                        "guidance-001"),
                RequirementId =
                    new RequirementId(
                        "requirement-001"),
                EvidenceClassification =
                    EvidenceClassifications.MedicalOpinion,
                GuidanceRole =
                    EvidenceGuidanceRoles.SupportsRequirement,
                Description =
                    "A medical opinion may help support the requirement."
            };

        Assert.Equal(
            "guidance-001",
            guidance.Id.Value);

        Assert.Equal(
            "requirement-001",
            guidance.RequirementId.Value);

        Assert.Equal(
            EvidenceClassifications.MedicalOpinion,
            guidance.EvidenceClassification);

        Assert.Equal(
            EvidenceGuidanceRoles.SupportsRequirement,
            guidance.GuidanceRole);

        Assert.Equal(
            "A medical opinion may help support the requirement.",
            guidance.Description);
    }

    [Fact]
    public void GuidanceRoles_ExposeDefinedValues()
    {
        Assert.Equal(
            "SupportsRequirement",
            EvidenceGuidanceRoles.SupportsRequirement);

        Assert.Equal(
            "EstablishesElement",
            EvidenceGuidanceRoles.EstablishesElement);

        Assert.Equal(
            "Corroborates",
            EvidenceGuidanceRoles.Corroborates);

        Assert.Equal(
            "Clarifies",
            EvidenceGuidanceRoles.Clarifies);
    }

    [Fact]
    public void GuidanceId_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(
            () => new EvidenceRequirementGuidanceId(""));
    }

    [Fact]
    public void GuidanceId_RejectsWhitespaceValue()
    {
        Assert.Throws<ArgumentException>(
            () => new EvidenceRequirementGuidanceId("   "));
    }
}

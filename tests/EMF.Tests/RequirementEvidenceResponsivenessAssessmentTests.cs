using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class RequirementEvidenceResponsivenessAssessmentTests
{
    [Fact]
    public void MissingItems_PreservesGuidanceMetadata()
    {
        var requirementId =
            new RequirementId("requirement-001");

        var matching =
            new RequirementEvidenceResponsivenessItem
            {
                Guidance =
                    new EvidenceRequirementGuidance
                    {
                        Id =
                            new EvidenceRequirementGuidanceId(
                                "guidance-001"),
                        RequirementId = requirementId,
                        EvidenceClassification =
                            EvidenceClassifications.MedicalOpinion,
                        GuidanceRole =
                            EvidenceGuidanceRoles.EstablishesElement,
                        Description = "Medical opinion evidence."
                    },
                HasMatchingEvidence = true
            };

        var missing =
            new RequirementEvidenceResponsivenessItem
            {
                Guidance =
                    new EvidenceRequirementGuidance
                    {
                        Id =
                            new EvidenceRequirementGuidanceId(
                                "guidance-002"),
                        RequirementId = requirementId,
                        EvidenceClassification =
                            EvidenceClassifications.ServiceRecord,
                        GuidanceRole =
                            EvidenceGuidanceRoles.Corroborates,
                        Description = "Service record evidence."
                    },
                HasMatchingEvidence = false
            };

        var assessment =
            new RequirementEvidenceResponsivenessAssessment
            {
                RequirementId = requirementId,
                Items = new[] { matching, missing }
            };

        Assert.Single(assessment.MatchingItems);
        Assert.Equal(1, assessment.MatchingItemCount);
        Assert.Equal(1, assessment.MissingItemCount);

        var missingItem =
            Assert.Single(assessment.MissingItems);

        Assert.Equal(
            EvidenceClassifications.ServiceRecord,
            missingItem.Guidance.EvidenceClassification);

        Assert.Equal(
            EvidenceGuidanceRoles.Corroborates,
            missingItem.Guidance.GuidanceRole);

        Assert.Equal(
            "Service record evidence.",
            missingItem.Guidance.Description);
    }
}

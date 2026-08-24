using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransEvidenceChecklistFormatterTests
{
    [Fact]
    public void Format_ProducesRequirementEvidenceLines()
    {
        var checklist =
            new ClaimIssueEvidenceChecklist
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-formatter-1"),
                RequirementChecklists =
                    new[]
                    {
                        new EvidenceDevelopmentChecklist
                        {
                            RequirementId =
                                new RequirementId("requirement-formatter-1"),
                            Items =
                                new[]
                                {
                                    new EvidenceDevelopmentChecklistItem
                                    {
                                        RequirementId =
                                            new RequirementId(
                                                "requirement-formatter-1"),
                                        EvidenceClassification =
                                            EvidenceClassifications.MedicalOpinion,
                                        GuidanceRole =
                                            EvidenceGuidanceRoles.SupportsRequirement,
                                        Description =
                                            "Medical opinion evidence."
                                    }
                                }
                        }
                    }
            };

        var lines =
            VeteransEvidenceChecklistFormatter.Format(
                checklist);

        Assert.Equal(
            new[]
            {
                "Claim Issue: issue-formatter-1",
                "Requirement: requirement-formatter-1",
                "- MedicalOpinion / SupportsRequirement: " +
                    "Medical opinion evidence."
            },
            lines);
    }
}

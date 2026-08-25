using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimEvidenceDetailsFormatterTests
{
    [Fact]
    public void Format_IncludesClaimIssueEvidenceDetails()
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-001"),
                ClaimId = new ClaimId("claim-001"),
                ClaimIssueType = "ServiceConnection"
            };

        var details =
            new ClaimEvidenceDetails
            {
                Claim =
                    new Claim
                    {
                        Id = issue.ClaimId,
                        VeteranId = new VeteranId("veteran-001")
                    },
                Issues =
                    new[]
                    {
                        new ClaimIssueEvidenceDetails
                        {
                            ClaimIssue = issue,
                            Checklist =
                                new ClaimIssueEvidenceChecklist
                                {
                                    ClaimIssueId = issue.Id,
                                    RequirementChecklists =
                                        new[]
                                        {
                                            new EvidenceDevelopmentChecklist
                                            {
                                                RequirementId =
                                                    new RequirementId(
                                                        "requirement-001"),
                                                Items =
                                                    new[]
                                                    {
                                                        new EvidenceDevelopmentChecklistItem
                                                        {
                                                            RequirementId =
                                                                new RequirementId(
                                                                    "requirement-001"),
                                                            EvidenceClassification =
                                                                EvidenceClassifications.MedicalOpinion,
                                                            GuidanceRole =
                                                                EvidenceGuidanceRoles.SupportsRequirement,
                                                            Description =
                                                                "Medical opinion."
                                                        }
                                                    }
                                            }
                                        }
                                },
                            DevelopmentPlans = []
                        }
                    }
            };

        var lines =
            VeteransClaimEvidenceDetailsFormatter.Format(details);

        Assert.Contains(
            "Claim: claim-001",
            lines);

        Assert.Contains(
            "Issue: issue-001 (ServiceConnection)",
            lines);

        Assert.Contains(
            "Requirement: requirement-001",
            lines);

        Assert.Contains(
            "- MedicalOpinion / SupportsRequirement: Medical opinion.",
            lines);

        Assert.Contains(
            "Development plans: 0",
            lines);
    }
}

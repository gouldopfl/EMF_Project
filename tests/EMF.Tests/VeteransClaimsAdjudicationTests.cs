using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsAdjudicationTests
{
    [Fact]
    public void MedicalOpinion_PreservesIssueAndOpinion()
    {
        var opinion = new MedicalOpinion
        {
            Id = new MedicalOpinionId("opinion-001"),
            ClaimIssueId = new ClaimIssueId("claim-issue-001"),
            Question = "Is the condition related?",
            Opinion = "Professional medical opinion"
        };

        Assert.Equal("Professional medical opinion", opinion.Opinion);
    }

    [Fact]
    public void LegalAnalysis_MayReferenceRequirement()
    {
        var requirementId = new RequirementId("requirement-001");

        var analysis = new LegalAnalysis
        {
            Id = new LegalAnalysisId("analysis-001"),
            ClaimIssueId = new ClaimIssueId("claim-issue-001"),
            RequirementId = requirementId,
            Analysis = "Applicable legal analysis"
        };

        Assert.Equal(requirementId, analysis.RequirementId);
    }

    [Fact]
    public void Finding_PreservesOutcomeWithoutBecomingDecision()
    {
        var finding = new Finding
        {
            Id = new FindingId("finding-001"),
            ClaimIssueId = new ClaimIssueId("claim-issue-001"),
            Outcome = FindingOutcomes.Favorable,
            Description = "Requirement supported by evidence"
        };

        Assert.Equal(FindingOutcomes.Favorable, finding.Outcome);
    }
}

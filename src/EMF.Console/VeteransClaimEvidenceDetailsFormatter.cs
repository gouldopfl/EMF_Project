using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.ConsoleApplication;

internal static class VeteransClaimEvidenceDetailsFormatter
{
    public static IReadOnlyList<string> Format(
        ClaimEvidenceDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var lines = new List<string>
        {
            $"Claim: {details.Claim.Id.Value}"
        };

        foreach (var issue in details.Issues)
        {
            lines.Add(
                $"Issue: {issue.ClaimIssue.Id.Value} " +
                $"({issue.ClaimIssue.ClaimIssueType})");

            foreach (var requirement in
                issue.Checklist.RequirementChecklists)
            {
                lines.Add(
                    $"Requirement: {requirement.RequirementId.Value}");

                foreach (var item in requirement.Items)
                {
                    lines.Add(
                        $"- {item.EvidenceClassification} / " +
                        $"{item.GuidanceRole}: {item.Description}");
                }
            }

            lines.Add(
                $"Development plans: {issue.DevelopmentPlans.Count}");
        }

        return lines;
    }
}

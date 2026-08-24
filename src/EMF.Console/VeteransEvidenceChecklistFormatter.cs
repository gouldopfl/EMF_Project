using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.ConsoleApplication;

internal static class VeteransEvidenceChecklistFormatter
{
    public static IReadOnlyList<string> Format(
        ClaimIssueEvidenceChecklist checklist)
    {
        ArgumentNullException.ThrowIfNull(checklist);

        var lines = new List<string>
        {
            $"Claim Issue: {checklist.ClaimIssueId.Value}"
        };

        foreach (var requirement in checklist.RequirementChecklists)
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

        return lines;
    }
}

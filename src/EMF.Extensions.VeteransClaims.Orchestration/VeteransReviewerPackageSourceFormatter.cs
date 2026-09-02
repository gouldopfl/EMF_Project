using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerPackageSourceFormatter
{
    public static string Format(
        ClaimIssueAdjudicationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var builder = new StringBuilder();

        builder.AppendLine(
            $"Claim Issue: {details.ClaimIssue.Id.Value}");

        builder.AppendLine(
            $"Claim Type: {details.ClaimIssue.ClaimIssueType}");

        builder.AppendLine();
        builder.AppendLine("Claimed Conditions:");

        foreach (var condition in details.ClaimedConditions)
        {
            builder.AppendLine(
                $"- {condition.Id.Value}: {condition.Name}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Service-Connection Theories:");

        foreach (var theory in details.ServiceConnectionTheories)
        {
            builder.AppendLine(
                $"- {theory.Id.Value}: {theory.TheoryType}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Service-Connection Bases:");

        foreach (var basis in details.ServiceConnectionBases)
        {
            builder.AppendLine(
                $"- {basis.Id.Value}: theory " +
                $"{basis.ServiceConnectionTheoryId.Value}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Service-Connected Conditions:");

        foreach (var item in
            details.ServiceConnectedConditions)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.ServiceConnectedCondition.Id.Value}: " +
                $"{item.ServiceConnectedCondition.Name}");
        }

        builder.AppendLine();
        builder.AppendLine("Service Events:");

        foreach (var item in details.ServiceEvents)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.ServiceEvent.Id.Value}: " +
                $"{item.ServiceEvent.Description}");
        }

        builder.AppendLine();
        builder.AppendLine("Requirements:");

        foreach (var item in details.Requirements)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.Requirement.Id.Value}: " +
                $"{item.Requirement.Description}");

            builder.AppendLine(
                $"  Regulation: " +
                $"{item.RegulatoryProvision.Citation}");

            builder.AppendLine(
                $"  Evidence Responsiveness: " +
                $"{item.Responsiveness.MatchingItemCount} matching, " +
                $"{item.Responsiveness.MissingItemCount} missing");

            if (item.DevelopmentChecklist.Items.Count > 0)
            {
                builder.AppendLine(
                    "  Outstanding Evidence:");

                foreach (var checklistItem in
                    item.DevelopmentChecklist.Items)
                {
                    builder.AppendLine(
                        $"  - {checklistItem.EvidenceClassification} / " +
                        $"{checklistItem.GuidanceRole}: " +
                        $"{checklistItem.Description}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Claim-Issue Evidence:");
        builder.AppendLine("  Evidence Checklist:");

        foreach (var requirement in
            details.Evidence.Checklist.RequirementChecklists)
        {
            builder.AppendLine(
                $"  - requirement " +
                $"{requirement.RequirementId.Value}");

            foreach (var item in requirement.Items)
            {
                builder.AppendLine(
                    $"    - {item.EvidenceClassification} / " +
                    $"{item.GuidanceRole}: " +
                    $"{item.Description}");
            }
        }

        builder.AppendLine("  Development Plans:");

        foreach (var plan in
            details.Evidence.DevelopmentPlans)
        {
            builder.AppendLine(
                $"  - {plan.Id.Value}: " +
                $"{plan.Description}");
        }

        builder.AppendLine();
        builder.AppendLine("Timeline:");

        foreach (var item in details.Timeline)
        {
            builder.Append(
                $"- {item.OccurredAt:O} | {item.EventType}");

            if (!string.IsNullOrWhiteSpace(
                item.ReferenceId))
            {
                builder.Append(
                    $" | reference {item.ReferenceId}");
            }

            if (!string.IsNullOrWhiteSpace(item.Outcome))
            {
                builder.Append(
                    $" | outcome {item.Outcome}");
            }

            if (!string.IsNullOrWhiteSpace(
                item.Description))
            {
                builder.Append(
                    $": {item.Description}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }
}

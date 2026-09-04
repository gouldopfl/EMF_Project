using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueAdjudicationReadinessService
{
    public ClaimIssueAdjudicationReadiness Assess(
        ClaimIssueAdjudicationDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        foreach (var requirement in details.Requirements)
        {
            if (requirement.Basis.ClaimIssueId !=
                details.ClaimIssue.Id)
            {
                throw new InvalidOperationException(
                    "Readiness requirement claim issue mismatch.");
            }

            if (requirement.DevelopmentChecklist.RequirementId !=
                requirement.Requirement.Id)
            {
                throw new InvalidOperationException(
                    "Readiness checklist requirement mismatch.");
            }

            if (requirement.Responsiveness.RequirementId !=
                requirement.Requirement.Id)
            {
                throw new InvalidOperationException(
                    "Readiness responsiveness requirement mismatch.");
            }

            if (requirement.DevelopmentChecklist.Items.Any(
                x => x.RequirementId != requirement.Requirement.Id))
            {
                throw new InvalidOperationException(
                    "Readiness checklist item requirement mismatch.");
            }

            if (requirement.RegulatoryProvision.Id !=
                requirement.Requirement.RegulatoryProvisionId)
            {
                throw new InvalidOperationException(
                    "Readiness regulatory provision mismatch.");
            }
        }

        var blocking =
            details.Requirements
                .Where(
                    x =>
                        x.DevelopmentChecklist
                            .HasOutstandingItems)
                .ToArray();

        return new ClaimIssueAdjudicationReadiness
        {
            ClaimIssueId = details.ClaimIssue.Id,
            BlockingRequirements = blocking
        };
    }
}

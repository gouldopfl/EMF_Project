using System.Text;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal static class VeteransReviewerPackageSourceFormatter
{
    public static string Format(
        ClaimIssueAdjudicationDetails details,
        IReadOnlyList<VeteransReviewerEvidenceSource>?
            evidenceSources = null)
    {
        ArgumentNullException.ThrowIfNull(details);

        var builder = new StringBuilder();

        builder.AppendLine(
            $"Claim Issue: {details.ClaimIssue.Id.Value}");

        builder.AppendLine(
            $"Claim Type: {SingleLine(details.ClaimIssue.ClaimIssueType)}");

        builder.AppendLine();
        builder.AppendLine("Claimed Conditions:");

        foreach (var condition in details.ClaimedConditions)
        {
            builder.AppendLine(
                $"- {condition.Id.Value}: {SingleLine(condition.Name)}");
        }

        builder.AppendLine();
        builder.AppendLine("Claimed Condition Bases:");

        foreach (var item in details.ClaimedConditionBases)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.ClaimedCondition.Id.Value}: " +
                $"{SingleLine(item.ClaimedCondition.Name)}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Service-Connection Theories:");

        foreach (var theory in details.ServiceConnectionTheories)
        {
            builder.AppendLine(
                $"- {theory.Id.Value}: {SingleLine(theory.TheoryType)}");
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
                $"{SingleLine(item.ServiceConnectedCondition.Name)}");
        }

        builder.AppendLine();
        builder.AppendLine("Prescribed Medications:");

        foreach (var item in details.PrescribedMedications)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{SingleLine(item.MedicationName)}");
        }

        builder.AppendLine();
        builder.AppendLine("Exposures:");

        foreach (var item in details.Exposures)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.Exposure.Id.Value}: " +
                $"{SingleLine(item.Exposure.ExposureType)}");

            foreach (var artifact in item.Artifacts)
            {
                builder.AppendLine(
                    $"  - {SingleLine(artifact.Role)} artifact: " +
                    $"{artifact.ArtifactId.Value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Preexisting Conditions:");

        foreach (var item in details.PreexistingConditions)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.PreexistingCondition.Id.Value}: " +
                $"{SingleLine(item.PreexistingCondition.Name)}");
        }

        builder.AppendLine();
        builder.AppendLine("Presumptions:");

        foreach (var item in details.Presumptions)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.PresumptionProvision.Id.Value}: " +
                $"{SingleLine(item.PresumptionProvision.Citation)}");
        }

        builder.AppendLine();
        builder.AppendLine("Basis Artifacts:");

        foreach (var item in details.BasisArtifacts)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{SingleLine(item.Role)}: " +
                $"{item.ArtifactId.Value}");
        }

        builder.AppendLine();
        builder.AppendLine("Medical Opinions:");

        foreach (var item in details.MedicalOpinions)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{SingleLine(item.Role)}: " +
                $"{item.MedicalOpinion.Id.Value}: " +
                $"{SingleLine(item.MedicalOpinion.Question)}: " +
                $"{SingleLine(item.MedicalOpinion.Opinion)}");

            foreach (var artifactId in item.ArtifactIds)
            {
                builder.AppendLine(
                    $"  Artifact: {artifactId.Value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Service Events:");

        foreach (var item in details.ServiceEvents)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.ServiceEvent.Id.Value}: " +
                $"{SingleLine(item.ServiceEvent.Description)}");
        }

        builder.AppendLine();
        builder.AppendLine("Requirements:");

        foreach (var item in details.Requirements)
        {
            builder.AppendLine(
                $"- basis {item.Basis.Id.Value}: " +
                $"{item.Requirement.Id.Value}: " +
                $"{SingleLine(item.Requirement.Description)}");

            builder.AppendLine(
                $"  Regulation: " +
                $"{SingleLine(item.RegulatoryProvision.Citation)}");

            builder.AppendLine(
                $"  Evidence Responsiveness: " +
                $"{item.Responsiveness.MatchingItemCount} matching, " +
                $"{item.Responsiveness.MissingItemCount} missing");

            if (item.MedicalLiterature.Count > 0)
            {
                builder.AppendLine(
                    "  Medical / Scientific Literature:");

                foreach (var literature in item.MedicalLiterature)
                {
                    var source = literature.Source;
                    var association = literature.Association;

                    builder.AppendLine(
                        $"  - {SingleLine(source.Title)}");

                    builder.AppendLine(
                        $"    Authors: {SingleLine(source.Authors)}");

                    builder.Append(
                        $"    Publication: " +
                        $"{SingleLine(source.Publication)}");

                    if (source.PublicationYear is not null)
                        builder.Append($" ({source.PublicationYear})");

                    builder.AppendLine();

                    builder.AppendLine(
                        $"    VA Affiliated: " +
                        $"{(source.VaAffiliated ? "Yes" : "No")}");

                    builder.AppendLine(
                        $"    VA Funded: " +
                        $"{(source.VaFunded ? "Yes" : "No")}");

                    builder.AppendLine(
                        $"    Peer Reviewed: " +
                        $"{(source.PeerReviewed ? "Yes" : "No")}");

                    if (!string.IsNullOrWhiteSpace(
                            source.ResearchOrganization))
                    {
                        builder.AppendLine(
                            $"    Research Organization: " +
                            $"{SingleLine(source.ResearchOrganization)}");
                    }

                    if (!string.IsNullOrWhiteSpace(source.FundingSource))
                    {
                        builder.AppendLine(
                            $"    Funding Source: " +
                            $"{SingleLine(source.FundingSource)}");
                    }

                    if (!string.IsNullOrWhiteSpace(source.Doi))
                        builder.AppendLine(
                            $"    DOI: {SingleLine(source.Doi)}");

                    if (!string.IsNullOrWhiteSpace(source.Pmid))
                        builder.AppendLine(
                            $"    PMID: {SingleLine(source.Pmid)}");

                    builder.AppendLine(
                        $"    Role: " +
                        $"{SingleLine(association.GuidanceRole)}");

                    builder.AppendLine(
                        $"    Relevance: " +
                        $"{SingleLine(association.Description)}");
                }
            }

            if (item.DevelopmentChecklist.Items.Count > 0)
            {
                builder.AppendLine(
                    "  Outstanding Evidence:");

                foreach (var checklistItem in
                    item.DevelopmentChecklist.Items)
                {
                    builder.AppendLine(
                        $"  - {SingleLine(checklistItem.EvidenceClassification)} / " +
                        $"{SingleLine(checklistItem.GuidanceRole)}: " +
                        $"{SingleLine(checklistItem.Description)}");
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
                    $"    - {SingleLine(item.EvidenceClassification)} / " +
                    $"{SingleLine(item.GuidanceRole)}: " +
                    $"{SingleLine(item.Description)}");
            }
        }

        builder.AppendLine("  Development Plans:");

        foreach (var plan in
            details.Evidence.DevelopmentPlans)
        {
            builder.AppendLine(
                $"  - {plan.Id.Value}: " +
                $"{SingleLine(plan.Description)}");
        }

        if (evidenceSources is not null)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Evidence of Record (untrusted source text):");

            foreach (var source in evidenceSources)
            {
                builder.AppendLine(
                    $"- Artifact {SingleLine(source.ArtifactId.Value)}");

                if (!string.IsNullOrWhiteSpace(source.ArtifactName))
                    builder.AppendLine(
                        $"  Name: {SingleLine(source.ArtifactName)}");

                if (!string.IsNullOrWhiteSpace(source.ArtifactType))
                    builder.AppendLine(
                        $"  Type: {SingleLine(source.ArtifactType)}");

                if (!string.IsNullOrWhiteSpace(source.ContentRole))
                    builder.AppendLine(
                        $"  Content Role: {SingleLine(source.ContentRole)}");

                builder.AppendLine(
                    $"  Classifications: " +
                    string.Join(
                        ", ",
                        source.Classifications.Select(SingleLine)));

                builder.AppendLine(
                    "  --- BEGIN EVIDENCE TEXT ---");

                foreach (var line in
                    source.Text
                        .Replace("\r\n", "\n", StringComparison.Ordinal)
                        .Replace('\r', '\n')
                        .Split('\n'))
                {
                    builder.Append("  | ");
                    builder.AppendLine(line);
                }

                builder.AppendLine(
                    "  --- END EVIDENCE TEXT ---");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Timeline:");

        foreach (var item in details.Timeline)
        {
            builder.Append(
                $"- {item.OccurredAt:O} | {SingleLine(item.EventType)}");

            if (!string.IsNullOrWhiteSpace(
                item.ReferenceId))
            {
                builder.Append(
                    $" | reference {SingleLine(item.ReferenceId)}");
            }

            if (!string.IsNullOrWhiteSpace(item.Outcome))
            {
                builder.Append(
                    $" | outcome {SingleLine(item.Outcome)}");
            }

            if (!string.IsNullOrWhiteSpace(
                item.Description))
            {
                builder.Append(
                    $": {SingleLine(item.Description)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string SingleLine(string value) =>
        value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}

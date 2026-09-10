using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageBasisScopeService
{
    private readonly IEvidenceDevelopmentPlanRepository _development;
    private readonly IEvidenceGapRepository _gaps;

    public VeteransReviewerPackageBasisScopeService(
        IEvidenceDevelopmentPlanRepository development,
        IEvidenceGapRepository gaps)
    {
        ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(gaps);

        _development = development;
        _gaps = gaps;
    }

    public async Task<VeteransReviewerPackageBasisScope> ScopeAsync(
        ClaimIssueAdjudicationDetails details,
        IReadOnlyList<VeteransReviewerEvidenceDevelopmentDetails>
            developmentDetails,
        ServiceConnectionBasisId basisId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(developmentDetails);

        var claimIssueId = details.ClaimIssue.Id;

        var basis =
            details.ServiceConnectionBases.SingleOrDefault(
                x => x.Id == basisId)
            ?? throw new InvalidOperationException(
                $"Service-connection basis not found: {basisId.Value}");

        if (basis.ClaimIssueId != claimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer basis belongs to another claim issue.");
        }

        var theory =
            details.ServiceConnectionTheories.SingleOrDefault(
                x => x.Id == basis.ServiceConnectionTheoryId)
            ?? throw new InvalidOperationException(
                "Reviewer basis theory was not found.");

        if (theory.ClaimIssueId != claimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer basis theory belongs to another claim issue.");
        }

        var scopedRequirements =
            details.Requirements
                .Where(x => x.Basis.Id == basisId)
                .ToArray();

        var requirementIds =
            scopedRequirements
                .Select(x => x.Requirement.Id)
                .ToHashSet();

        var scopedPlans =
            await ScopeDevelopmentPlansAsync(
                details.Evidence.DevelopmentPlans,
                claimIssueId,
                requirementIds,
                cancellationToken);

        var scopedChecklist =
            new ClaimIssueEvidenceChecklist
            {
                ClaimIssueId = claimIssueId,
                RequirementChecklists =
                    details.Evidence.Checklist.RequirementChecklists
                        .Where(
                            x => requirementIds.Contains(
                                x.RequirementId))
                        .ToArray()
            };

        var scopedDetails =
            new ClaimIssueAdjudicationDetails
            {
                ClaimIssue = details.ClaimIssue,
                ClaimedConditions = details.ClaimedConditions,
                ClaimedConditionBases =
                    details.ClaimedConditionBases
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                ServiceConnectionTheories = [theory],
                ServiceConnectionBases = [basis],
                ServiceConnectedConditions =
                    details.ServiceConnectedConditions
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                PrescribedMedications =
                    details.PrescribedMedications
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                Exposures =
                    details.Exposures
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                PreexistingConditions =
                    details.PreexistingConditions
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                Presumptions =
                    details.Presumptions
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                MedicalOpinions =
                    details.MedicalOpinions
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                BasisArtifacts =
                    details.BasisArtifacts
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                ServiceEvents =
                    details.ServiceEvents
                        .Where(x => x.Basis.Id == basisId)
                        .ToArray(),
                Requirements = scopedRequirements,
                Evidence =
                    new ClaimIssueEvidenceDetails
                    {
                        ClaimIssue = details.Evidence.ClaimIssue,
                        Checklist = scopedChecklist,
                        DevelopmentPlans = scopedPlans
                    },
                Timeline = details.Timeline
            };

        var scopedDevelopmentDetails =
            developmentDetails
                .Where(
                    x => requirementIds.Contains(
                        x.Gap.RequirementId))
                .ToArray();

        return new VeteransReviewerPackageBasisScope
        {
            Details = scopedDetails,
            DevelopmentDetails = scopedDevelopmentDetails
        };
    }

    private async Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        ScopeDevelopmentPlansAsync(
            IReadOnlyList<EvidenceDevelopmentPlan> plans,
            ClaimIssueId claimIssueId,
            IReadOnlySet<RequirementId> requirementIds,
            CancellationToken cancellationToken)
    {
        var scoped =
            new List<EvidenceDevelopmentPlan>();

        foreach (var plan in plans)
        {
            if (plan.ClaimIssueId != claimIssueId)
            {
                throw new InvalidOperationException(
                    "Reviewer development plan belongs to another claim issue.");
            }

            var links =
                await _development
                    .GetEvidenceDevelopmentPlanEvidenceGapsAsync(
                        plan.Id,
                        cancellationToken);

            if (links.Any(
                    x => x.EvidenceDevelopmentPlanId != plan.Id))
            {
                throw new InvalidOperationException(
                    "Reviewer development plan gap lineage mismatch.");
            }

            var include = false;

            foreach (var link in links)
            {
                var gap =
                    await _gaps.GetEvidenceGapAsync(
                        link.EvidenceGapId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Reviewer development gap was not found.");

                if (gap.Id != link.EvidenceGapId)
                {
                    throw new InvalidOperationException(
                        "Reviewer development gap identity mismatch.");
                }

                if (gap.ClaimIssueId != claimIssueId)
                {
                    throw new InvalidOperationException(
                        "Reviewer development gap belongs to another claim issue.");
                }

                if (requirementIds.Contains(gap.RequirementId))
                    include = true;
            }

            if (include)
                scoped.Add(plan);
        }

        return scoped;
    }
}

public sealed class VeteransReviewerPackageBasisScope
{
    public required ClaimIssueAdjudicationDetails Details { get; init; }

    public required IReadOnlyList<VeteransReviewerEvidenceDevelopmentDetails>
        DevelopmentDetails { get; init; }
}

using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceRecognitionTermBasisProposalResult
{
    public required ServiceConnectionBasisId BasisId
    { get; init; }

    public required ClaimedConditionId ClaimedConditionId
    { get; init; }

    public required RequirementId RequirementId
    { get; init; }

    public required string ClaimedCondition
    { get; init; }

    public required IReadOnlyList<string> ServiceConnectedConditions
    { get; init; }

    public required EvidenceRecognitionTermProposalResult ProposalResult
    { get; init; }
}

public sealed class EvidenceRecognitionTermProposalCoordinator
{
    private readonly IClaimIssueAdjudicationDetailsService _details;
    private readonly EvidenceRecognitionTermProposalService _proposalService;

    public EvidenceRecognitionTermProposalCoordinator(
        IClaimIssueAdjudicationDetailsService details,
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string> executor)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(executor);

        _details = details;
        _proposalService =
            new EvidenceRecognitionTermProposalService(executor);
    }

    public async Task<IReadOnlyList<EvidenceRecognitionTermBasisProposalResult>>
        ProposeAsync(
            ClaimIssueId claimIssueId,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var details =
            await _details.GetAsync(
                claimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Claim issue '{claimIssueId.Value}' was not found.");

        ValidateDetails(details, claimIssueId);

        var results =
            new List<EvidenceRecognitionTermBasisProposalResult>();

        foreach (var basis in
            details.ServiceConnectionBases
                .OrderBy(x => x.Id.Value, StringComparer.Ordinal))
        {
            var claimedConditions =
                details.ClaimedConditionBases
                    .Where(x => x.Basis.Id == basis.Id)
                    .OrderBy(
                        x => x.ClaimedCondition.Id.Value,
                        StringComparer.Ordinal)
                    .ToArray();

            var requirements =
                details.Requirements
                    .Where(x => x.Basis.Id == basis.Id)
                    .OrderBy(
                        x => x.Requirement.Id.Value,
                        StringComparer.Ordinal)
                    .ToArray();

            if (requirements.Length != 0 &&
                claimedConditions.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Service-connection basis '{basis.Id.Value}' has " +
                    "requirements but no claimed condition.");
            }

            if (requirements.Length == 0)
                continue;

            var serviceConnectedConditions =
                details.ServiceConnectedConditions
                    .Where(x => x.Basis.Id == basis.Id)
                    .Select(x => x.ServiceConnectedCondition.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            foreach (var claimedCondition in claimedConditions)
            {
                foreach (var requirement in requirements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var proposal =
                        await _proposalService.ProposeAsync(
                            requirement.Requirement,
                            claimedCondition.ClaimedCondition.Name,
                            serviceConnectedConditions,
                            context,
                            cancellationToken);

                    results.Add(
                        new EvidenceRecognitionTermBasisProposalResult
                        {
                            BasisId = basis.Id,
                            ClaimedConditionId =
                                claimedCondition.ClaimedCondition.Id,
                            RequirementId = requirement.Requirement.Id,
                            ClaimedCondition =
                                claimedCondition.ClaimedCondition.Name,
                            ServiceConnectedConditions =
                                serviceConnectedConditions,
                            ProposalResult = proposal
                        });
                }
            }
        }

        return results;
    }

    private static void ValidateDetails(
        ClaimIssueAdjudicationDetails details,
        ClaimIssueId claimIssueId)
    {
        if (details.ClaimIssue.Id != claimIssueId)
            throw new InvalidOperationException(
                "Claim issue adjudication details identity mismatch.");

        if (details.ClaimedConditions.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Claimed condition claim issue mismatch.");
        }

        if (details.ClaimedConditions.Count !=
            details.ClaimedConditions
                .DistinctBy(x => x.Id)
                .Count())
        {
            throw new InvalidOperationException(
                "Claimed condition details contain duplicate identities.");
        }

        if (details.ServiceConnectionBases.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Service-connection basis claim issue mismatch.");
        }

        if (details.ServiceConnectionBases.Count !=
            details.ServiceConnectionBases
                .DistinctBy(x => x.Id)
                .Count())
        {
            throw new InvalidOperationException(
                "Service-connection basis details contain duplicate identities.");
        }

        var basisIds =
            details.ServiceConnectionBases
                .Select(x => x.Id)
                .ToHashSet();

        var claimedConditionIds =
            details.ClaimedConditions
                .Select(x => x.Id)
                .ToHashSet();

        if (details.ClaimedConditionBases.Any(
            x => !basisIds.Contains(x.Basis.Id) ||
                 x.Basis.ClaimIssueId != claimIssueId ||
                 !claimedConditionIds.Contains(x.ClaimedCondition.Id) ||
                 x.ClaimedCondition.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Basis claimed-condition details are outside the claim issue lineage.");
        }

        if (details.ClaimedConditionBases.Count !=
            details.ClaimedConditionBases
                .DistinctBy(
                    x => (x.Basis.Id, x.ClaimedCondition.Id))
                .Count())
        {
            throw new InvalidOperationException(
                "Basis claimed-condition details contain duplicate associations.");
        }

        if (details.ServiceConnectedConditions.Any(
            x => !basisIds.Contains(x.Basis.Id) ||
                 x.Basis.ClaimIssueId != claimIssueId ||
                 string.IsNullOrWhiteSpace(
                     x.ServiceConnectedCondition.Name)))
        {
            throw new InvalidOperationException(
                "Service-connected condition details are invalid for the claim issue.");
        }

        if (details.ServiceConnectedConditions.Count !=
            details.ServiceConnectedConditions
                .DistinctBy(
                    x =>
                        (x.Basis.Id,
                         x.ServiceConnectedCondition.Id))
                .Count())
        {
            throw new InvalidOperationException(
                "Service-connected condition details contain duplicate associations.");
        }

        if (details.Requirements.Any(
            x => !basisIds.Contains(x.Basis.Id) ||
                 x.Basis.ClaimIssueId != claimIssueId ||
                 x.Requirement.RegulatoryProvisionId !=
                    x.RegulatoryProvision.Id))
        {
            throw new InvalidOperationException(
                "Basis requirement details are outside the claim issue or regulatory lineage.");
        }

        if (details.Requirements.Count !=
            details.Requirements
                .DistinctBy(
                    x => (x.Basis.Id, x.Requirement.Id))
                .Count())
        {
            throw new InvalidOperationException(
                "Basis requirement details contain duplicate associations.");
        }
    }
}

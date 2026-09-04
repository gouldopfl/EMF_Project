using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ClaimIssueMeritsAssessmentService
{
    private readonly IServiceConnectionRepository _serviceConnections;
    private readonly RequirementFindingAssessmentService _findings;
    private readonly RequirementFindingOutcomeAssessmentService _requirementOutcomes;
    private readonly ServiceConnectionBasisOutcomeAssessmentService _basisOutcomes;
    private readonly ServiceConnectionTheoryOutcomeAssessmentService _theoryOutcomes;
    private readonly ClaimIssueMeritsOutcomeAssessmentService _claimIssueOutcomes;

    public ClaimIssueMeritsAssessmentService(
        IServiceConnectionRepository serviceConnections,
        IFindingRepository findings)
    {
        ArgumentNullException.ThrowIfNull(serviceConnections);
        ArgumentNullException.ThrowIfNull(findings);

        _serviceConnections = serviceConnections;
        _findings =
            new RequirementFindingAssessmentService(findings);
        _requirementOutcomes =
            new RequirementFindingOutcomeAssessmentService();
        _basisOutcomes =
            new ServiceConnectionBasisOutcomeAssessmentService();
        _theoryOutcomes =
            new ServiceConnectionTheoryOutcomeAssessmentService();
        _claimIssueOutcomes =
            new ClaimIssueMeritsOutcomeAssessmentService();
    }

    public async Task<ClaimIssueMeritsOutcomeAssessment> AssessAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        var theories =
            await _serviceConnections
                .GetServiceConnectionTheoriesAsync(
                    claimIssueId,
                    cancellationToken);

        if (theories.Any(
            x => x.ClaimIssueId != claimIssueId))
        {
            throw new InvalidOperationException(
                "Merits theory claim issue mismatch.");
        }

        var theoryAssessments =
            new List<ServiceConnectionTheoryOutcomeAssessment>();

        foreach (var theory in theories)
        {
            var bases =
                await _serviceConnections
                    .GetServiceConnectionBasesAsync(
                        theory.Id,
                        cancellationToken);

            if (bases.Any(
                x =>
                    x.ClaimIssueId != claimIssueId ||
                    x.ServiceConnectionTheoryId != theory.Id))
            {
                throw new InvalidOperationException(
                    "Merits basis ownership mismatch.");
            }

            var basisAssessments =
                new List<ServiceConnectionBasisOutcomeAssessment>();

            foreach (var basis in bases)
            {
                var requirementIds =
                    await _serviceConnections.GetRequirementIdsAsync(
                        basis.Id,
                        cancellationToken);

                var grouped =
                    await _findings.AssessAsync(
                        claimIssueId,
                        requirementIds,
                        cancellationToken);

                var requirementAssessments =
                    grouped
                        .Select(_requirementOutcomes.Assess)
                        .ToArray();

                basisAssessments.Add(
                    _basisOutcomes.Assess(
                        basis,
                        requirementAssessments));
            }

            theoryAssessments.Add(
                _theoryOutcomes.Assess(
                    theory,
                    basisAssessments));
        }

        return _claimIssueOutcomes.Assess(
            claimIssueId,
            theoryAssessments);
    }
}

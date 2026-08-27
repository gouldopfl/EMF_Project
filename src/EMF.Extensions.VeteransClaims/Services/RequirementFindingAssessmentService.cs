using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RequirementFindingAssessmentService
{
    private readonly IFindingRepository _findings;

    public RequirementFindingAssessmentService(
        IFindingRepository findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        _findings = findings;
    }

    public async Task<IReadOnlyList<RequirementFindingAssessment>>
        AssessAsync(
            ClaimIssueId claimIssueId,
            IReadOnlyCollection<RequirementId> requirementIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirementIds);

        var findings =
            await _findings.GetFindingsAsync(
                claimIssueId,
                cancellationToken);

        return requirementIds
            .Distinct()
            .Select(
                requirementId =>
                    new RequirementFindingAssessment
                    {
                        RequirementId = requirementId,
                        Findings =
                            findings
                                .Where(
                                    x =>
                                        x.RequirementId ==
                                        requirementId)
                                .ToArray()
                    })
            .ToArray();
    }
}

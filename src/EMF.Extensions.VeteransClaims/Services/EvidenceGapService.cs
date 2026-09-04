using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidenceGapService : IEvidenceGapService
{
    private readonly IEvidenceGapRepository _repository;
    private readonly IRequirementEvidenceService _requirements;
    private readonly IIdGenerator _idGenerator;

    public EvidenceGapService(
        IEvidenceGapRepository repository,
        IRequirementEvidenceService requirements,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(idGenerator);

        _repository = repository;
        _requirements = requirements;
        _idGenerator = idGenerator;
    }

    public async Task<EvidenceGap?> EnsureGapAsync(
        ClaimIssueId claimIssueId,
        RequirementId requirementId,
        CancellationToken cancellationToken = default)
    {
        var checklist =
            await _requirements.CreateChecklistAsync(
                requirementId,
                cancellationToken);

        if (checklist.RequirementId != requirementId)
        {
            throw new InvalidOperationException(
                "Evidence gap checklist requirement mismatch.");
        }

        if (checklist.Items.Any(
            x => x.RequirementId != requirementId))
        {
            throw new InvalidOperationException(
                "Evidence gap checklist item requirement mismatch.");
        }

        if (!checklist.HasOutstandingItems)
            return null;

        var existing =
            await _repository.GetEvidenceGapsAsync(
                requirementId,
                cancellationToken);

        var mismatchedRequirement =
            existing.FirstOrDefault(
                x => x.RequirementId != requirementId);

        if (mismatchedRequirement is not null)
        {
            throw new InvalidOperationException(
                $"Requirement '{requirementId.Value}' gap lookup returned " +
                $"gap '{mismatchedRequirement.Id.Value}' for requirement " +
                $"'{mismatchedRequirement.RequirementId.Value}'.");
        }

        var matching =
            existing.FirstOrDefault(
                x => x.ClaimIssueId == claimIssueId);

        if (matching is not null)
            return matching;

        var gap = new EvidenceGap
        {
            Id = new EvidenceGapId(_idGenerator.Generate()),
            ClaimIssueId = claimIssueId,
            RequirementId = requirementId,
            Description = "Missing supporting evidence.",
            Status = EvidenceGapStatuses.Open
        };

        await _repository.AddEvidenceGapAsync(
            gap,
            cancellationToken);

        return gap;
    }
}

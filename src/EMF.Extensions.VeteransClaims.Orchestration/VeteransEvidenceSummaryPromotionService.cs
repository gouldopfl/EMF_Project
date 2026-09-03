using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Core.Models;
using EMF.Intelligence.Agents;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class VeteransEvidenceSummaryPromotionService :
    IVeteransEvidenceSummaryPromotionService
{
    private readonly IIntelligenceEvidencePromotionService
        _promotionService;

    public VeteransEvidenceSummaryPromotionService(
        IIntelligenceEvidencePromotionService promotionService)
    {
        ArgumentNullException.ThrowIfNull(promotionService);

        _promotionService = promotionService;
    }

    public async Task<Artifact> PromoteAsync(
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        ClaimIssueId claimIssueId,
        IntelligenceAgentResult<string> result,
        CancellationToken cancellationToken = default)
    {
        var artifact =
            new TextSummaryEvidenceArtifactFactory()
                .Create(
                    result.Output!,
                    name,
                    promotedUtc);

        artifact = new Artifact
        {
            Id = artifact.Id,
            Name = artifact.Name,
            ArtifactType = artifact.ArtifactType,
            Fingerprint = artifact.Fingerprint,
            CreatedUtc = artifact.CreatedUtc,
            Metadata = new Dictionary<string, object>(
                artifact.Metadata)
            {
                ["claimIssueId"] = claimIssueId.Value
            }
        };

        await _promotionService.PromoteAsync(
            new IntelligenceEvidencePromotionRequest<string>
            {
                Artifact = artifact,
                IntelligenceResult = result,
                PromotedBy = promotedBy,
                PromotedUtc = promotedUtc,
                ReviewedBy = reviewedBy,
                ReviewedUtc = promotedUtc
            },
            cancellationToken);

        return artifact;
    }

    public async Task<Artifact> PromoteAsync(
        string name,
        string promotedBy,
        string reviewedBy,
        DateTimeOffset promotedUtc,
        EvidenceGapId evidenceGapId,
        RequirementId requirementId,
        IntelligenceAgentResult<string> result,
        CancellationToken cancellationToken = default)
    {
        var artifact =
            new TextSummaryEvidenceArtifactFactory()
                .Create(
                    result.Output!,
                    name,
                    promotedUtc);

        artifact = new Artifact
        {
            Id = artifact.Id,
            Name = artifact.Name,
            ArtifactType = artifact.ArtifactType,
            Fingerprint = artifact.Fingerprint,
            CreatedUtc = artifact.CreatedUtc,
            Metadata = new Dictionary<string, object>(artifact.Metadata)
            {
                ["evidenceGapId"] = evidenceGapId.Value,
                ["requirementId"] = requirementId.Value
            }
        };

        await _promotionService.PromoteAsync(
            new IntelligenceEvidencePromotionRequest<string>
            {
                Artifact = artifact,
                IntelligenceResult = result,
                PromotedBy = promotedBy,
                PromotedUtc = promotedUtc,
                ReviewedBy = reviewedBy,
                ReviewedUtc = promotedUtc
            },
            cancellationToken);

        return artifact;
    }
}

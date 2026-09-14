using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ReviewedBoundedEvidenceInterpretation
{
    public required ClaimIssueId ClaimIssueId { get; init; }

    public required ServiceConnectionBasisId ServiceConnectionBasisId
    { get; init; }

    public required RequirementId RequirementId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Direction { get; init; }

    public required string OpinionStandard { get; init; }

    public required string MedicalConclusion { get; init; }

    public required string RationaleSummary { get; init; }

    public required string PromotedBy { get; init; }

    public required DateTimeOffset PromotedUtc { get; init; }

    public required string ReviewedBy { get; init; }

    public required DateTimeOffset ReviewedUtc { get; init; }

    public required string CapabilityId { get; init; }

    public required string ProviderId { get; init; }

    public required string CorrelationId { get; init; }

    public required string EngineName { get; init; }

    public string? EngineVersion { get; init; }

    public string? ProviderOperationId { get; init; }

    public int? InputTokenCount { get; init; }

    public int? OutputTokenCount { get; init; }

    public int? TotalTokenCount { get; init; }

    public decimal? EstimatedCostUsd { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }

    public required bool RequiresReview { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<ReviewedBoundedEvidenceSourceExcerpt>
        SourceExcerpts { get; init; }
}

public sealed class ReviewedBoundedEvidenceSourceExcerpt
{
    public required ArtifactId ArtifactId { get; init; }

    public required string Text { get; init; }

    public required int StartOffset { get; init; }

    public required int Length { get; init; }
}

using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class ReviewedMedicalLiteratureClassification
{
    public required RequirementMedicalLiterature Association { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string PromotedBy { get; init; }

    public required DateTimeOffset PromotedUtc { get; init; }

    public required string ReviewedBy { get; init; }

    public required DateTimeOffset ReviewedUtc { get; init; }

    public required string IntelligenceOutput { get; init; }

    public required string CapabilityId { get; init; }

    public required string ProviderId { get; init; }

    public required string CorrelationId { get; init; }

    public required string EngineName { get; init; }

    public string? EngineVersion { get; init; }

    public string? ProviderOperationId { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }

    public required bool RequiresReview { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<MedicalLiteratureSourceExcerpt>
        SourceExcerpts { get; init; }
}

using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Models;

public sealed class IntelligenceExecutionMetadata
{
    public required IntelligenceCapabilityId
        CapabilityId { get; init; }

    public required IntelligenceProviderId
        ProviderId { get; init; }

    public required IntelligenceCorrelationId
        CorrelationId { get; init; }

    public required string EngineName { get; init; }

    public string? EngineVersion { get; init; }

    public string? ProviderOperationId { get; init; }

    public int? InputTokenCount { get; init; }

    public int? OutputTokenCount { get; init; }

    public int? TotalTokenCount { get; init; }

    public decimal? InputCostUsdPerMillionTokens { get; init; }

    public decimal? OutputCostUsdPerMillionTokens { get; init; }

    public decimal? EstimatedCostUsd { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }
}

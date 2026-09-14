namespace EMF.Intelligence.AzureOpenAI.Models;

internal sealed record AzureOpenAITextCompletion(
    string Text,
    string? ModelVersion = null,
    string? ProviderOperationId = null,
    string? FinishReason = null,
    int? InputTokenCount = null,
    int? OutputTokenCount = null,
    int? TotalTokenCount = null,
    decimal? InputCostUsdPerMillionTokens = null,
    decimal? OutputCostUsdPerMillionTokens = null,
    decimal? EstimatedCostUsd = null);

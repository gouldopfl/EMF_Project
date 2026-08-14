namespace EMF.Intelligence.AzureOpenAI.Models;

internal sealed record AzureOpenAITextCompletion(
    string Text,
    string? ModelVersion = null,
    string? ProviderOperationId = null,
    string? FinishReason = null);

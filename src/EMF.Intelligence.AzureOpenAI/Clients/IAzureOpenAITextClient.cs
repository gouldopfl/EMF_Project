using EMF.Intelligence.AzureOpenAI.Models;

namespace EMF.Intelligence.AzureOpenAI.Clients;

internal interface IAzureOpenAITextClient
{
    Task<AzureOpenAITextCompletion> CompleteAsync(
        string systemInstruction,
        string input,
        CancellationToken cancellationToken = default);
}

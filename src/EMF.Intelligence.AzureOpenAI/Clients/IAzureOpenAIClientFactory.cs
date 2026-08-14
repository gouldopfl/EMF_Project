using Azure.AI.OpenAI;

namespace EMF.Intelligence.AzureOpenAI.Clients;

internal interface IAzureOpenAIClientFactory
{
    AzureOpenAIClient CreateClient();
}

using Azure.AI.OpenAI;
using Azure.Identity;
using EMF.Intelligence.AzureOpenAI.Configuration;
using System.ClientModel.Primitives;

namespace EMF.Intelligence.AzureOpenAI.Clients;

internal sealed class AzureOpenAIClientFactory :
    IAzureOpenAIClientFactory
{
    private readonly AzureOpenAIOptions _options;

    public AzureOpenAIClientFactory(
        AzureOpenAIOptions options)
    {
        AzureOpenAIOptionsValidator.Validate(options);

        _options = options;
    }

    public AzureOpenAIClient CreateClient()
    {
        var credentialOptions =
            new DefaultAzureCredentialOptions();

        if (!string.IsNullOrWhiteSpace(
                _options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId =
                _options.ManagedIdentityClientId;
        }

        var clientOptions =
            new AzureOpenAIClientOptions
            {
                NetworkTimeout =
                    _options.RequestTimeout,
                RetryPolicy =
                    new ClientRetryPolicy(
                        _options.MaximumRetries)
            };

        return new AzureOpenAIClient(
            new Uri(_options.Endpoint),
            new DefaultAzureCredential(
                credentialOptions),
            clientOptions);
    }
}

using EMF.Intelligence.AzureOpenAI.Configuration;

namespace EMF.ConsoleApplication;

internal static class AzureOpenAIConsoleOptionsFactory
{
    public static AzureOpenAIOptions Create()
    {
        var endpoint =
            Require("EMF_AZURE_OPENAI_ENDPOINT");

        var deployment =
            Require("EMF_AZURE_OPENAI_DEPLOYMENT");

        var timeoutSeconds =
            ParseInteger(
                "EMF_AZURE_OPENAI_TIMEOUT_SECONDS",
                120);

        var maximumRetries =
            ParseInteger(
                "EMF_AZURE_OPENAI_MAX_RETRIES",
                2);

        return new AzureOpenAIOptions
        {
            Endpoint = endpoint,
            DeploymentName = deployment,
            ProviderId =
                Environment.GetEnvironmentVariable(
                    "EMF_AZURE_OPENAI_PROVIDER_ID") ??
                "azure.openai",
            ManagedIdentityClientId =
                Environment.GetEnvironmentVariable(
                    "EMF_AZURE_OPENAI_MANAGED_IDENTITY_CLIENT_ID"),
            RequestTimeout =
                TimeSpan.FromSeconds(timeoutSeconds),
            MaximumRetries = maximumRetries
        };
    }

    private static string Require(string name)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Set {name} before using Azure OpenAI.");
        }

        return value;
    }

    private static int ParseInteger(
        string name,
        int defaultValue)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!int.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException(
                $"{name} must be an integer.");
        }

        return parsed;
    }
}

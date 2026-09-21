using System.Globalization;
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

        var liveCallsEnabled =
            ParseBoolean(
                "EMF_AZURE_OPENAI_LIVE",
                false);

        var useMaxCompletionTokensProperty =
            ParseBoolean(
                "EMF_AZURE_OPENAI_USE_MAX_COMPLETION_TOKENS",
                false);

        var reasoningEffort =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_REASONING_EFFORT");

        var inputCost =
            ParseOptionalDecimal(
                "EMF_AZURE_OPENAI_INPUT_COST_USD_PER_MILLION");

        var outputCost =
            ParseOptionalDecimal(
                "EMF_AZURE_OPENAI_OUTPUT_COST_USD_PER_MILLION");

        if (liveCallsEnabled &&
            (!inputCost.HasValue || !outputCost.HasValue))
        {
            throw new InvalidOperationException(
                "Live Azure OpenAI calls require both " +
                "EMF_AZURE_OPENAI_INPUT_COST_USD_PER_MILLION " +
                "and EMF_AZURE_OPENAI_OUTPUT_COST_USD_PER_MILLION.");
        }

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
            MaximumRetries = maximumRetries,
            LiveCallsEnabled = liveCallsEnabled,
            UseMaxCompletionTokensProperty =
                useMaxCompletionTokensProperty,
            ReasoningEffort = reasoningEffort,
            InputCostUsdPerMillionTokens = inputCost,
            OutputCostUsdPerMillionTokens = outputCost
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

    private static bool ParseBoolean(
        string name,
        bool defaultValue)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (!bool.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException(
                $"{name} must be true or false.");
        }

        return parsed;
    }

    private static decimal? ParseOptionalDecimal(
        string name)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0)
        {
            throw new InvalidOperationException(
                $"{name} must be a nonnegative decimal.");
        }

        return parsed;
    }
}

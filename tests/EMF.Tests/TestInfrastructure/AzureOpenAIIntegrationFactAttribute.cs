using Xunit;

namespace EMF.Tests.TestInfrastructure;

public sealed class AzureOpenAIIntegrationFactAttribute :
    FactAttribute
{
    public AzureOpenAIIntegrationFactAttribute()
    {
        var enabled =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_LIVE_TESTS");

        var endpoint =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_ENDPOINT");

        var deployment =
            Environment.GetEnvironmentVariable(
                "EMF_AZURE_OPENAI_DEPLOYMENT");

        if (!string.Equals(
                enabled,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Azure OpenAI live tests are disabled.";
        }
        else if (string.IsNullOrWhiteSpace(endpoint) ||
                 string.IsNullOrWhiteSpace(deployment))
        {
            Skip = "Azure OpenAI live configuration is incomplete.";
        }
    }
}

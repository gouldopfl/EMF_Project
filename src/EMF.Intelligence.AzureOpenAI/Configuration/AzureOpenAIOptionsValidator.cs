namespace EMF.Intelligence.AzureOpenAI.Configuration;

internal static class AzureOpenAIOptionsValidator
{
    public static void Validate(
        AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(
                options.Endpoint,
                UriKind.Absolute,
                out var endpoint) ||
            !string.Equals(
                endpoint.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            !endpoint.IsDefaultPort ||
            !endpoint.Host.EndsWith(
                ".openai.azure.com",
                StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.Length <= ".openai.azure.com".Length ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            endpoint.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "An absolute HTTPS endpoint is required.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.DeploymentName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.ProviderId);

        ValidateManagedIdentityClientId(
            options.ManagedIdentityClientId);

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options));
        }

        if (options.MaximumRetries is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options));
        }

        ValidateCostRates(options);
    }

    private static void ValidateCostRates(
        AzureOpenAIOptions options)
    {
        var inputRate =
            options.InputCostUsdPerMillionTokens;

        var outputRate =
            options.OutputCostUsdPerMillionTokens;

        if (inputRate.HasValue != outputRate.HasValue)
        {
            throw new ArgumentException(
                "Azure OpenAI input and output cost rates " +
                "must be configured together.",
                nameof(options));
        }

        if (inputRate is < 0 || outputRate is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Azure OpenAI cost rates cannot be negative.");
        }
    }

    private static void ValidateManagedIdentityClientId(
        string? clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId) &&
            (!Guid.TryParseExact(
                clientId,
                "D",
                out var parsedClientId) ||
             parsedClientId == Guid.Empty))
        {
            throw new ArgumentException(
                "Managed identity client ID must be a nonempty GUID.",
                nameof(clientId));
        }
    }
}

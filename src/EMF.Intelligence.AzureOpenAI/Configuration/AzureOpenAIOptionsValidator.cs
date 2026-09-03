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
    }
}

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
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "An absolute HTTPS endpoint is required.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.DeploymentName);

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

namespace EMF.Security.Azure.Configuration;

internal static class AzureMonitorAlertOptionsValidator
{
    public static Uri Validate(
        AzureMonitorAlertOptions options)
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
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "Azure Monitor endpoint must be an absolute HTTPS URI.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.RuleId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.StreamName);

        return endpoint;
    }
}

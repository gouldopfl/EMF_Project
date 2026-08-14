namespace EMF.Intelligence.AzureOpenAI.Exceptions;

internal static class AzureOpenAIFailureClassifier
{
    public static AzureOpenAIFailureKind Classify(
        int statusCode)
    {
        return statusCode switch
        {
            0 => AzureOpenAIFailureKind.Transport,
            401 =>
                AzureOpenAIFailureKind.Authentication,
            403 =>
                AzureOpenAIFailureKind.Authorization,
            408 or 504 =>
                AzureOpenAIFailureKind.Timeout,
            429 =>
                AzureOpenAIFailureKind.Throttling,
            _ => AzureOpenAIFailureKind.Provider
        };
    }
}

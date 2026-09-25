namespace EMF.Intelligence.AzureOpenAI.Exceptions;

public sealed class AzureOpenAIProviderException :
    Exception
{
    internal AzureOpenAIProviderException(
        AzureOpenAIFailureKind failureKind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message)
        // SDK exceptions can contain response bodies, prompts, and credentials.
        // Only the normalized failure kind and HTTP status may cross this boundary.
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
    }

    public AzureOpenAIFailureKind FailureKind
    { get; }

    public int? StatusCode { get; }
}

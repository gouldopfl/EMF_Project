namespace EMF.Intelligence.AzureOpenAI.Exceptions;

public sealed class AzureOpenAIProviderException :
    Exception
{
    internal AzureOpenAIProviderException(
        AzureOpenAIFailureKind failureKind,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
    }

    public AzureOpenAIFailureKind FailureKind
    { get; }

    public int? StatusCode { get; }
}

namespace EMF.ConsoleApplication;

internal sealed class
    TextSummarizationProviderException :
    Exception
{
    public TextSummarizationProviderException(
        string failureKind,
        int? statusCode,
        Exception innerException)
        : base(
            statusCode is null
                ? $"Text summarization provider failed: {failureKind}."
                : $"Text summarization provider failed: {failureKind} " +
                  $"(HTTP {statusCode}).",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            failureKind);
        ArgumentNullException.ThrowIfNull(
            innerException);

        FailureKind = failureKind;
        StatusCode = statusCode;
    }

    public string FailureKind { get; }

    public int? StatusCode { get; }
}

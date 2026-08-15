namespace EMF.ConsoleApplication;

internal sealed class
    TextSummarizationProviderException :
    Exception
{
    public TextSummarizationProviderException(
        string failureKind,
        Exception innerException)
        : base(
            $"Text summarization provider failed: " +
            $"{failureKind}.",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            failureKind);
        ArgumentNullException.ThrowIfNull(
            innerException);

        FailureKind = failureKind;
    }

    public string FailureKind { get; }
}

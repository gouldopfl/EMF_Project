namespace EMF.Intelligence.AzureOpenAI.Exceptions;

public sealed class
    AzureOpenAIInvalidResponseException :
    Exception
{
    public AzureOpenAIInvalidResponseException(
        string reason)
        : base(reason)
    {
    }
}

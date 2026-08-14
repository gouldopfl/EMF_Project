namespace EMF.Intelligence.AzureOpenAI.Exceptions;

public enum AzureOpenAIFailureKind
{
    Authentication,
    Authorization,
    Throttling,
    Timeout,
    Transport,
    Provider
}

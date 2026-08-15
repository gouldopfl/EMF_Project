using EMF.Intelligence.AzureOpenAI.Exceptions;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.ConsoleApplication;

internal sealed class
    AzureOpenAITextSummarizationExecutorAdapter :
    IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string>
{
    private readonly
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> _inner;

    public AzureOpenAITextSummarizationExecutorAdapter(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    public async Task<
        IntelligenceCapabilityResult<string>>
        ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        try
        {
            return await _inner.ExecuteAsync(
                capabilityId,
                request,
                context,
                cancellationToken);
        }
        catch (AzureOpenAIProviderException exception)
        {
            throw new TextSummarizationProviderException(
                exception.FailureKind.ToString(),
                exception);
        }
    }
}

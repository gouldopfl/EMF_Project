using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Exceptions;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.AzureOpenAI.Providers;

public sealed class
    AzureOpenAITextSummarizationProvider :
    IIntelligenceCapabilityProvider<
        TextSummarizationRequest,
        string>
{
    private readonly IAzureOpenAITextClient
        _textClient;

    private readonly string _deploymentName;

    public AzureOpenAITextSummarizationProvider(
        AzureOpenAIOptions options)
        : this(
            new AzureOpenAITextClient(
                new AzureOpenAIClientFactory(options),
                options),
            options)
    {
    }

    internal AzureOpenAITextSummarizationProvider(
        IAzureOpenAITextClient textClient,
        AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(textClient);
        AzureOpenAIOptionsValidator.Validate(options);

        _textClient = textClient;
        _deploymentName = options.DeploymentName;
        ProviderId =
            new IntelligenceProviderId(
                options.ProviderId);
    }

    public IntelligenceCapabilityId Id =>
        IntelligenceCapabilityIds.TextSummarization;

    public IntelligenceProviderId ProviderId
    {
        get;
    }

    public async Task<
        IntelligenceCapabilityResult<string>>
        ExecuteAsync(
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var instruction =
            "Summarize the supplied text faithfully " +
            $"in no more than {request.MaximumCharacters} " +
            "characters. Return only the summary.";

        var completion =
            await _textClient.CompleteAsync(
                instruction,
                request.Text,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(
                completion.Text))
        {
            throw new
                AzureOpenAIInvalidResponseException(
                    "The provider returned no summary.");
        }

        if (completion.Text.Length >
            request.MaximumCharacters)
        {
            throw new
                AzureOpenAIInvalidResponseException(
                    "The provider summary exceeded " +
                    "the requested character limit.");
        }

        var warnings =
            completion.FinishReason is null or "Stop"
                ? Array.Empty<string>()
                :
                [
                    "Provider completion finished with " +
                    $"reason '{completion.FinishReason}'."
                ];

        return new IntelligenceCapabilityResult<string>
        {
            Success = true,
            Message =
                "Azure OpenAI summary generated.",
            Output = completion.Text,
            Metadata =
                new IntelligenceExecutionMetadata
                {
                    CapabilityId = Id,
                    ProviderId = ProviderId,
                    CorrelationId =
                        context.CorrelationId,
                    EngineName = _deploymentName,
                    EngineVersion =
                        completion.ModelVersion,
                    ProviderOperationId =
                        completion.ProviderOperationId,
                    StartedUtc = startedUtc,
                    CompletedUtc =
                        DateTimeOffset.UtcNow
                },
            SourceArtifactIds =
                context.InputArtifactIds.ToArray(),
            Warnings = warnings,
            RequiresReview = true
        };
    }
}

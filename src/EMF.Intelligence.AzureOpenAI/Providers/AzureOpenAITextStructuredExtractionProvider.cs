using System.Text.Json;
using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Exceptions;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.AzureOpenAI.Providers;

public sealed class
    AzureOpenAITextStructuredExtractionProvider :
    IIntelligenceCapabilityProvider<
        TextStructuredExtractionRequest,
        string>
{
    private readonly IAzureOpenAITextClient
        _textClient;

    private readonly string _deploymentName;

    public AzureOpenAITextStructuredExtractionProvider(
        AzureOpenAIOptions options)
        : this(
            new AzureOpenAITextClient(
                new AzureOpenAIClientFactory(options),
                options),
            options)
    {
    }

    internal
        AzureOpenAITextStructuredExtractionProvider(
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
        IntelligenceCapabilityIds
            .TextStructuredExtraction;

    public IntelligenceProviderId ProviderId
    {
        get;
    }

    public async Task<
        IntelligenceCapabilityResult<string>>
        ExecuteAsync(
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;

        var instruction =
            request.Instruction +
            " Return only valid JSON matching this shape: " +
            request.JsonSchema;

        var completion =
            await _textClient.CompleteAsync(
                instruction,
                request.Text,
                cancellationToken);

        try
        {
            using var document =
                JsonDocument.Parse(completion.Text);
        }
        catch (JsonException)
        {
            throw new AzureOpenAIInvalidResponseException(
                "The provider returned invalid JSON.");
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
                "Azure OpenAI structured extraction generated.",
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

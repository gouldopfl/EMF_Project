using Azure.Identity;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Exceptions;
using System.ClientModel;
using EMF.Intelligence.AzureOpenAI.Models;
using OpenAI.Chat;

namespace EMF.Intelligence.AzureOpenAI.Clients;

internal sealed class AzureOpenAITextClient :
    IAzureOpenAITextClient
{
    private readonly ChatClient? _chatClient;
    private readonly bool _liveCallsEnabled;
    private readonly decimal? _inputCostUsdPerMillionTokens;
    private readonly decimal? _outputCostUsdPerMillionTokens;

    public AzureOpenAITextClient(
        IAzureOpenAIClientFactory clientFactory,
        AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            clientFactory);
        AzureOpenAIOptionsValidator.Validate(options);

        _liveCallsEnabled = options.LiveCallsEnabled;
        _inputCostUsdPerMillionTokens =
            options.InputCostUsdPerMillionTokens;
        _outputCostUsdPerMillionTokens =
            options.OutputCostUsdPerMillionTokens;

        if (!_liveCallsEnabled)
            return;

        _chatClient =
            clientFactory.CreateClient()
                .GetChatClient(
                    options.DeploymentName);
    }

    public async Task<AzureOpenAITextCompletion>
        CompleteAsync(
            string systemInstruction,
            string input,
            CancellationToken cancellationToken =
                default,
            int? maximumOutputTokenCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            systemInstruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (!_liveCallsEnabled)
        {
            throw new InvalidOperationException(
                "Azure OpenAI live calls are disabled. " +
                "Explicitly enable live calls before invoking the provider.");
        }

        var chatClient =
            _chatClient ??
            throw new InvalidOperationException(
                "Azure OpenAI client is not configured for live calls.");

        if (maximumOutputTokenCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOutputTokenCount));
        }

        var completionOptions =
            new ChatCompletionOptions
            {
                MaxOutputTokenCount =
                    maximumOutputTokenCount
            };

        ClientResult<ChatCompletion> response;

        try
        {
            response =
                await chatClient.CompleteChatAsync(
                    [
                        new SystemChatMessage(
                            systemInstruction),
                        new UserChatMessage(input)
                    ],
                    completionOptions,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            throw;
        }
        catch (CredentialUnavailableException)
        {
            throw CreateFailure(
                AzureOpenAIFailureKind.Authentication);
        }
        catch (AuthenticationFailedException)
        {
            throw CreateFailure(
                AzureOpenAIFailureKind.Authentication);
        }
        catch (TaskCanceledException)
        {
            throw CreateFailure(
                AzureOpenAIFailureKind.Timeout);
        }
        catch (ClientResultException exception)
        {
            throw CreateFailure(
                AzureOpenAIFailureClassifier.Classify(
                    exception.Status),
                exception.Status,
                exception);
        }
        catch (HttpRequestException)
        {
            throw CreateFailure(
                AzureOpenAIFailureKind.Transport);
        }

        var completion = response.Value;

        var text =
            string.Concat(
                    completion.Content.Select(
                        part => part.Text))
                .Trim();

        var rawResponse = response.GetRawResponse();

        string? operationId = null;

        if (!rawResponse.Headers.TryGetValue(
                "apim-request-id",
                out operationId))
        {
            rawResponse.Headers.TryGetValue(
                "x-request-id",
                out operationId);
        }

        var usage = completion.Usage;

        var inputTokenCount =
            usage?.InputTokenCount;

        var outputTokenCount =
            usage?.OutputTokenCount;

        var totalTokenCount =
            usage?.TotalTokenCount;

        var estimatedCostUsd =
            AzureOpenAICostEstimator.EstimateUsd(
                inputTokenCount,
                outputTokenCount,
                _inputCostUsdPerMillionTokens,
                _outputCostUsdPerMillionTokens);

        return new AzureOpenAITextCompletion(
            text,
            ModelVersion: completion.Model,
            ProviderOperationId: operationId,
            FinishReason:
                completion.FinishReason.ToString(),
            InputTokenCount: inputTokenCount,
            OutputTokenCount: outputTokenCount,
            TotalTokenCount: totalTokenCount,
            InputCostUsdPerMillionTokens:
                _inputCostUsdPerMillionTokens,
            OutputCostUsdPerMillionTokens:
                _outputCostUsdPerMillionTokens,
            EstimatedCostUsd: estimatedCostUsd);
    }

    private static AzureOpenAIProviderException
        CreateFailure(
            AzureOpenAIFailureKind failureKind,
            int? statusCode = null,
            Exception? innerException = null)
    {
        return new AzureOpenAIProviderException(
            failureKind,
            $"Azure OpenAI {failureKind.ToString().ToLowerInvariant()} failure.",
            statusCode,
            innerException);
    }
}

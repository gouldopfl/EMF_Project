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
    private readonly ChatClient _chatClient;

    public AzureOpenAITextClient(
        IAzureOpenAIClientFactory clientFactory,
        AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            clientFactory);
        AzureOpenAIOptionsValidator.Validate(options);

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
                default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            systemInstruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        ClientResult<ChatCompletion> response;

        try
        {
            response =
                await _chatClient.CompleteChatAsync(
                    [
                        new SystemChatMessage(
                            systemInstruction),
                        new UserChatMessage(input)
                    ],
                    cancellationToken:
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
                exception.Status);
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

        return new AzureOpenAITextCompletion(
            text,
            ModelVersion: completion.Model,
            ProviderOperationId: operationId,
            FinishReason:
                completion.FinishReason.ToString());
    }

    private static AzureOpenAIProviderException
        CreateFailure(
            AzureOpenAIFailureKind failureKind,
            int? statusCode = null)
    {
        return new AzureOpenAIProviderException(
            failureKind,
            $"Azure OpenAI {failureKind.ToString().ToLowerInvariant()} failure.",
            statusCode);
    }
}

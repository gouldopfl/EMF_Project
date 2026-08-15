using EMF.ConsoleApplication;
using EMF.Intelligence.AzureOpenAI.Exceptions;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    AzureOpenAITextSummarizationExecutorAdapterTests
{
    [Fact]
    public async Task ExecuteAsync_TranslatesAzureProviderFailure()
    {
        var azureFailure =
            new AzureOpenAIProviderException(
                AzureOpenAIFailureKind.Throttling,
                "Azure provider failed.",
                429);

        var adapter =
            new AzureOpenAITextSummarizationExecutorAdapter(
                new ThrowingExecutor(azureFailure));

        var exception =
            await Assert.ThrowsAsync<
                TextSummarizationProviderException>(
                () => adapter.ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        "Source text.",
                        100),
                    CreateContext()));

        Assert.Equal(
            "Throttling",
            exception.FailureKind);
        Assert.Same(
            azureFailure,
            exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTranslateUnrelatedFailure()
    {
        var failure =
            new InvalidOperationException(
                "Unrelated failure.");

        var adapter =
            new AzureOpenAITextSummarizationExecutorAdapter(
                new ThrowingExecutor(failure));

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => adapter.ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        "Source text.",
                        100),
                    CreateContext()));

        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var adapter =
            new AzureOpenAITextSummarizationExecutorAdapter(
                new ThrowingExecutor(
                    new OperationCanceledException(
                        cancellation.Token)));

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => adapter.ExecuteAsync(
                IntelligenceCapabilityIds
                    .TextSummarization,
                new TextSummarizationRequest(
                    "Source text.",
                    100),
                CreateContext(),
                cancellation.Token));
    }

    private static IntelligenceExecutionContext
        CreateContext()
    {
        return new IntelligenceExecutionContext(
            "console-steward",
            new IntelligenceCorrelationId(
                "adapter-test-001"),
            new ProtectionClassificationId(
                "public"),
            []);
    }

    private sealed class ThrowingExecutor :
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string>
    {
        private readonly Exception _failure;

        public ThrowingExecutor(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);

            _failure = failure;
        }

        public Task<
            IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextSummarizationRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            return Task.FromException<
                IntelligenceCapabilityResult<string>>(
                _failure);
        }
    }
}

using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class TextInsightAgentFailureTests
{
    [Fact]
    public async Task ExecuteAsync_StopsWhenSummaryFails()
    {
        var summaryExecutor =
            new StubCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceCapabilityResult<string>
                {
                    Success = false,
                    Message = "Summary failed.",
                    Metadata =
                        CreateMetadata(
                            IntelligenceCapabilityIds
                                .TextSummarization)
                });

        var keywordExecutor =
            new StubCapabilityExecutor<
                TextKeywordExtractionRequest,
                IReadOnlyList<TextKeyword>>(
                new IntelligenceCapabilityResult<
                    IReadOnlyList<TextKeyword>>
                {
                    Success = true,
                    Output = Array.Empty<TextKeyword>(),
                    Metadata =
                        CreateMetadata(
                            IntelligenceCapabilityIds
                                .TextKeywordExtraction)
                });

        var agent =
            new TextInsightAgent(
                summaryExecutor,
                keywordExecutor);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [],
                agent.Id);

        var result =
            await agent.ExecuteAsync(
                new TextInsightRequest(
                    "Evidence source text.",
                    100,
                    5),
                context);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Equal(
            "Summary failed.",
            result.Message);

        Assert.Single(
            result.CapabilityExecutions);

        Assert.Equal(1, summaryExecutor.CallCount);
        Assert.Equal(0, keywordExecutor.CallCount);
    }

    private static IntelligenceExecutionMetadata
        CreateMetadata(
            IntelligenceCapabilityId capabilityId)
    {
        var occurredUtc = DateTimeOffset.UtcNow;

        return new IntelligenceExecutionMetadata
        {
            CapabilityId = capabilityId,
            ProviderId =
                new IntelligenceProviderId(
                    "development.local"),
            CorrelationId =
                new IntelligenceCorrelationId(
                    "operation-001"),
            EngineName = "test-engine",
            StartedUtc = occurredUtc,
            CompletedUtc = occurredUtc
        };
    }

    private sealed class StubCapabilityExecutor<
        TRequest,
        TResult> :
        IIntelligenceCapabilityExecutor<
            TRequest,
            TResult>
        where TRequest : notnull
        where TResult : notnull
    {
        private readonly
            IntelligenceCapabilityResult<TResult>
            _result;

        public StubCapabilityExecutor(
            IntelligenceCapabilityResult<TResult>
                result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<
            IntelligenceCapabilityResult<TResult>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;

            return Task.FromResult(_result);
        }
    }
}

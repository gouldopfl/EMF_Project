using System.Diagnostics;
using EMF.Core.Contracts;
using EMF.Core.Diagnostics;
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
    private readonly ICommandMonitor? _monitor;
    private int _callCount;

    public AzureOpenAITextSummarizationExecutorAdapter(
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string> inner,
        ICommandMonitor? monitor = null)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _monitor = monitor;
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
        var callNumber =
            Interlocked.Increment(ref _callCount);

        var timer = Stopwatch.StartNew();

        Report(
            $"Call {callNumber} sending " +
            $"{request.Text.Length:N0} chars; " +
            $"max summary {request.MaximumCharacters:N0} chars");

        try
        {
            var result =
                await _inner.ExecuteAsync(
                    capabilityId,
                    request,
                    context,
                    cancellationToken);

            timer.Stop();

            Report(
                $"Call {callNumber} completed in " +
                $"{timer.Elapsed.TotalSeconds:F1}s");

            return result;
        }
        catch (AzureOpenAIProviderException exception)
        {
            timer.Stop();

            var status =
                exception.StatusCode is null
                    ? string.Empty
                    : $" HTTP {exception.StatusCode}";

            Report(
                $"Call {callNumber} FAILED after " +
                $"{timer.Elapsed.TotalSeconds:F1}s - " +
                $"{exception.FailureKind}{status}");

            throw new TextSummarizationProviderException(
                exception.FailureKind.ToString(),
                exception.StatusCode,
                exception);
        }
    }

    private void Report(string message)
    {
        _monitor?.Report(
            new CommandMonitorEvent(
                DateTimeOffset.UtcNow,
                "AI",
                message));
    }
}

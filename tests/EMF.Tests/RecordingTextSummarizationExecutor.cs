using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

internal sealed class RecordingTextSummarizationExecutor :
    IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string>
{
    public TextSummarizationRequest? Request
    { get; private set; }

    public IntelligenceExecutionContext? Context
    { get; private set; }

    public bool Success { get; set; } = true;

    public string? Output { get; set; } =
        "Reviewer summary";

    public Task<IntelligenceCapabilityResult<string>>
        ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
    {
        Request = request;
        Context = context;

        return Task.FromResult(
            new IntelligenceCapabilityResult<string>
            {
                Success = Success,
                Output = Output,
                RequiresReview = true,
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId = capabilityId,
                        ProviderId =
                            new IntelligenceProviderId(
                                "reviewer-test"),
                        CorrelationId =
                            context.CorrelationId,
                        EngineName = "reviewer-test",
                        StartedUtc = DateTimeOffset.UtcNow,
                        CompletedUtc = DateTimeOffset.UtcNow
                    }
            });
    }
}

using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Development.Providers;

public sealed class
    DevelopmentTextSummarizationProvider :
    IIntelligenceCapabilityProvider<
        TextSummarizationRequest,
        string>
{
    public IntelligenceCapabilityId Id =>
        IntelligenceCapabilityIds.TextSummarization;

    public IntelligenceProviderId ProviderId
    {
        get;
    } = new("development.local");

    public Task<
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

        var summary =
            request.Text.Length <=
                request.MaximumCharacters
                ? request.Text
                : request.MaximumCharacters == 1
                    ? "…"
                    : request.Text[
                        ..(request.MaximumCharacters - 1)]
                        .TrimEnd() + "…";

        var result =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Message =
                    "Development summary generated.",
                Output = summary,
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId = Id,
                        ProviderId = ProviderId,
                        CorrelationId =
                            context.CorrelationId,
                        EngineName =
                            "deterministic-extractive",
                        EngineVersion = "1.0",
                        StartedUtc = startedUtc,
                        CompletedUtc =
                            DateTimeOffset.UtcNow
                    },
                SourceArtifactIds =
                    context.InputArtifactIds.ToArray(),
                Warnings =
                [
                    "Development provider output is " +
                    "deterministic truncation, not " +
                    "semantic summarization."
                ],
                RequiresReview = true
            };

        return Task.FromResult(result);
    }
}

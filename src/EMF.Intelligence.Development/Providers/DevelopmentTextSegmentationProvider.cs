using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Development.Providers;

public sealed class
    DevelopmentTextSegmentationProvider :
    IIntelligenceCapabilityProvider<
        TextSegmentationRequest,
        IReadOnlyList<TextSegment>>
{
    public IntelligenceCapabilityId Id =>
        IntelligenceCapabilityIds.TextSegmentation;

    public IntelligenceProviderId ProviderId
    {
        get;
    } = new("development.local");

    public Task<
        IntelligenceCapabilityResult<
            IReadOnlyList<TextSegment>>>
        ExecuteAsync(
            TextSegmentationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        var startedUtc = DateTimeOffset.UtcNow;
        var segments = new List<TextSegment>();

        var step =
            request.MaximumSegmentCharacters -
            request.OverlapCharacters;

        var startOffset = 0;
        var index = 0;

        while (startOffset < request.Text.Length)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var length =
                Math.Min(
                    request.MaximumSegmentCharacters,
                    request.Text.Length - startOffset);

            segments.Add(
                new TextSegment(
                    index,
                    startOffset,
                    request.Text.Substring(
                        startOffset,
                        length)));

            if (startOffset + length >=
                request.Text.Length)
            {
                break;
            }

            startOffset += step;
            index++;
        }

        IReadOnlyList<TextSegment> output =
            segments.AsReadOnly();

        return Task.FromResult(
            new IntelligenceCapabilityResult<
                IReadOnlyList<TextSegment>>
            {
                Success = true,
                Message =
                    $"{segments.Count} text segments created.",
                Output = output,
                Metadata =
                    new IntelligenceExecutionMetadata
                    {
                        CapabilityId = Id,
                        ProviderId = ProviderId,
                        CorrelationId =
                            context.CorrelationId,
                        EngineName =
                            "deterministic-fixed-width",
                        EngineVersion = "1.0",
                        StartedUtc = startedUtc,
                        CompletedUtc =
                            DateTimeOffset.UtcNow
                    },
                SourceArtifactIds =
                    context.InputArtifactIds.ToArray(),
                Warnings =
                [
                    "Segments use fixed character " +
                    "boundaries."
                ]
            });
    }
}

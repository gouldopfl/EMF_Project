using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Security.Storage;
using EMF.Security.Storage.Models;

namespace EMF.Orchestration.Services;

public sealed class ArtifactEnvelopeRewrappingWorkflowActivity :
    IWorkflowActivity
{
    private readonly IArtifactEnvelopeRewrappingService
        _rewrappingService;
    private readonly ArtifactEnvelopeRewrappingRequest
        _request;

    public ArtifactEnvelopeRewrappingWorkflowActivity(
        IArtifactEnvelopeRewrappingService rewrappingService,
        ArtifactEnvelopeRewrappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(rewrappingService);
        ArgumentNullException.ThrowIfNull(request);

        _rewrappingService = rewrappingService;
        _request = request;
    }

    public string Id =>
        $"artifact-envelope-rewrap:{_request.ArtifactId.Value}";

    public string Name => "Artifact Envelope Rewrapping";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var result =
            await _rewrappingService.RewrapAsync(
                _request,
                cancellationToken);

        return new WorkflowActivityResult
        {
            Succeeded =
                result.Outcome !=
                ArtifactEnvelopeRewrappingOutcome.NotFound,
            Message = CreateMessage(result),
            CompletedUtc = result.CompletedUtc
        };
    }

    private static string CreateMessage(
        ArtifactEnvelopeRewrappingResult result)
    {
        return result.Outcome switch
        {
            ArtifactEnvelopeRewrappingOutcome.NotFound =>
                $"Artifact {result.ArtifactId.Value} was not found.",

            ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent =>
                $"Artifact {result.ArtifactId.Value} already uses the current key.",

            ArtifactEnvelopeRewrappingOutcome.Updated =>
                $"Artifact {result.ArtifactId.Value} envelope key was rewrapped.",

            _ => throw new InvalidOperationException(
                "Artifact envelope rewrapping returned an unknown outcome.")
        };
    }
}

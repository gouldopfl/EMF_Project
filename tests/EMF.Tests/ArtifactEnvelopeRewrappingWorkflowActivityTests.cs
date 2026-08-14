using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Security.Models.Identities;
using EMF.Security.Storage;
using EMF.Security.Storage.Models;

namespace EMF.Tests;

public sealed class ArtifactEnvelopeRewrappingWorkflowActivityTests
{
    [Fact]
    public async Task ExecuteAsync_reports_successful_rewrapping()
    {
        var completedUtc =
            new DateTimeOffset(
                2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

        var service =
            new FakeRewrappingService(
                new ArtifactEnvelopeRewrappingResult
                {
                    ArtifactId =
                        new ArtifactId("artifact-001"),
                    Outcome =
                        ArtifactEnvelopeRewrappingOutcome.Updated,
                    PreviousKeyEncryptionKeyId = "key-old",
                    CurrentKeyEncryptionKeyId = "key-current",
                    CompletedUtc = completedUtc
                });

        var request =
            new ArtifactEnvelopeRewrappingRequest
            {
                SubjectId = "operator-001",
                ArtifactId =
                    new ArtifactId("artifact-001"),
                ProtectionClassificationId =
                    new ProtectionClassificationId(
                        "restricted")
            };

        var activity =
            new ArtifactEnvelopeRewrappingWorkflowActivity(
                service,
                request);

        var result =
            await activity.ExecuteAsync(
                new WorkflowExecutionContext
                {
                    WorkflowId =
                        new WorkflowId("workflow-001")
                });

        Assert.Equal(
            "artifact-envelope-rewrap:artifact-001",
            activity.Id);
        Assert.True(result.Succeeded);
        Assert.Contains("was rewrapped", result.Message);
        Assert.Equal(completedUtc, result.CompletedUtc);
        Assert.Same(request, service.Request);
    }

    [Fact]
    public async Task ExecuteAsync_fails_when_artifact_is_missing()
    {
        var service =
            new FakeRewrappingService(
                new ArtifactEnvelopeRewrappingResult
                {
                    ArtifactId = new ArtifactId("missing"),
                    Outcome =
                        ArtifactEnvelopeRewrappingOutcome.NotFound,
                    CompletedUtc = DateTimeOffset.UtcNow
                });

        var activity =
            new ArtifactEnvelopeRewrappingWorkflowActivity(
                service,
                new ArtifactEnvelopeRewrappingRequest
                {
                    SubjectId = "operator-001",
                    ArtifactId = new ArtifactId("missing"),
                    ProtectionClassificationId =
                        new ProtectionClassificationId("restricted")
                });

        var result = await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-001")
            });

        Assert.False(result.Succeeded);
        Assert.Contains("was not found", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_succeeds_when_key_is_current()
    {
        var service =
            new FakeRewrappingService(
                new ArtifactEnvelopeRewrappingResult
                {
                    ArtifactId = new ArtifactId("artifact-001"),
                    Outcome =
                        ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent,
                    CompletedUtc = DateTimeOffset.UtcNow
                });

        var activity =
            new ArtifactEnvelopeRewrappingWorkflowActivity(
                service,
                new ArtifactEnvelopeRewrappingRequest
                {
                    SubjectId = "operator-001",
                    ArtifactId = new ArtifactId("artifact-001"),
                    ProtectionClassificationId =
                        new ProtectionClassificationId("restricted")
                });

        var result = await activity.ExecuteAsync(
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-001")
            });

        Assert.True(result.Succeeded);
        Assert.Contains("already uses", result.Message);
    }

    private sealed class FakeRewrappingService :
        IArtifactEnvelopeRewrappingService
    {
        private readonly ArtifactEnvelopeRewrappingResult
            _result;

        public FakeRewrappingService(
            ArtifactEnvelopeRewrappingResult result)
        {
            _result = result;
        }

        public ArtifactEnvelopeRewrappingRequest?
            Request
        { get; private set; }

        public Task<ArtifactEnvelopeRewrappingResult>
            RewrapAsync(
                ArtifactEnvelopeRewrappingRequest request,
                CancellationToken cancellationToken = default)
        {
            Request = request;

            return Task.FromResult(_result);
        }
    }
}

using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed partial class ArtifactEnvelopeRewrappingServiceTests
{
    [Fact]
    public async Task RewrapAsync_AuditsAuthorizationCancellation()
    {
        var cancellation =
            new OperationCanceledException(
                "Authorization cancelled.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            new ArtifactEnvelopeRewrappingService(
                new MissingContentStore(),
                new TestRewrappingService(),
                new FailingAuthorizationPolicy(cancellation),
                audit);

        var thrown =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-authorization-cancelled"))));

        Assert.Same(cancellation, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Null(record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsAuthorizationFailure()
    {
        var failure =
            new InvalidOperationException(
                "Authorization failed.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            new ArtifactEnvelopeRewrappingService(
                new MissingContentStore(),
                new TestRewrappingService(),
                new FailingAuthorizationPolicy(failure),
                audit);

        var thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-authorization-failed"))));

        Assert.Same(failure, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Null(record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Failed,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsContentReadCancellation()
    {
        var cancellation =
            new OperationCanceledException(
                "Content read cancelled.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            new ArtifactEnvelopeRewrappingService(
                new FailingReadContentStore(cancellation),
                new TestRewrappingService(),
                new AllowPolicy(),
                audit);

        var thrown =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-read-cancelled"))));

        Assert.Same(cancellation, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            AuthorizationDecision.Allow,
            record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsContentReadFailure()
    {
        var failure =
            new IOException("Content read failed.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            new ArtifactEnvelopeRewrappingService(
                new FailingReadContentStore(failure),
                new TestRewrappingService(),
                new AllowPolicy(),
                audit);

        var thrown =
            await Assert.ThrowsAsync<IOException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-read-failed"))));

        Assert.Same(failure, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            AuthorizationDecision.Allow,
            record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Failed,
            record.Outcome);
    }

    private sealed class FailingReadContentStore(
        Exception failure) : IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]?>(failure);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingAuthorizationPolicy(
        Exception failure) : IAuthorizationPolicy
    {
        public Task<AuthorizationDecision> EvaluateAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AuthorizationDecision>(failure);
    }
}
